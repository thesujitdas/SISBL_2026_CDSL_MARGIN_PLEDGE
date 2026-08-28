using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;
using SISBL_API_CDSL_MARGIN_PLEDGE.Interfaces;
using SISBL_API_CDSL_MARGIN_PLEDGE.Models;
using SISBL_COMMON.Models;
using System.Text;
using System.Threading.Channels;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Classes
{
    public class MarginPledgeClientProcess: IMarginPledgeClientProcess
    {
        enum StatusOptions { Started, Stopping, Stopped };
        enum logType
        {
            Gateway,
            HttpRequestData,
            HttpRequest,
            HttpResponse,
            HttpError,
            HttpRetry,
            ToRPC
        };
        const string conError = "Error ";
        const string conLogTime = "hh:mm:ss tt";
        static int temp = 1;

        IHttpClientFactory _httpClientFactory;
        HttpRequestRetryTracker httpConfigTracker;
        GrpcChannel marginPledge_Channel;
        MarginPledgeRPC.MarginPledgeRPCClient mpRpcClient;

        bool _isLive = false;
        string? _cdslEndpoint = string.Empty;

        static Channel<RepledgeReply> channel = Channel.CreateUnbounded<RepledgeReply>();
        ChannelWriter<RepledgeReply> mrWriter = channel.Writer;
        ChannelReader<RepledgeReply> mrReader = channel.Reader;
        SemaphoreSlim semaphore = new SemaphoreSlim(8, 32);

        static Channel<ToRPC> channelLog = Channel.CreateUnbounded<ToRPC>();
        ChannelWriter<ToRPC> logWriter = channelLog.Writer;
        ChannelReader<ToRPC> logReader = channelLog.Reader;

        CancellationTokenSource cts = new();
        CancellationTokenSource ctsRPC = new();
        Task? tskCDSL, tskRPC;


        public MarginPledgeClientProcess(IConfiguration Configuration, IHttpClientFactory httpClientFactory,
            IOptionsMonitor<HttpRequestRetryTracker> httpReqTracker)
        {
            _httpClientFactory = httpClientFactory;

            /* Configuration */
            var sisblConfig = Configuration.GetSection("SISBL");
            _isLive = sisblConfig.GetValue<string>("IsLIVE") == "1";


            /* HTTP request retry configuration */
            httpConfigTracker = httpReqTracker.CurrentValue;
            httpReqTracker.OnChange((tracker) =>
            {
                httpConfigTracker = tracker;
            });


            /* Http Endpoint */
            _cdslEndpoint = sisblConfig.GetValue<string>("CDSL_MarginRepledge_EndPoint");


            /* RPC Service */
            string? marginPledge_HostAddress;
            if (_isLive)
            {
                marginPledge_HostAddress = sisblConfig.GetValue<string>("RPC_MarginRepledge_ServiceHost_LIVE");
            }
            else
            {
                marginPledge_HostAddress = sisblConfig.GetValue<string>("RPC_MarginRepledge_ServiceHost_UAT");
            }

            var socketHttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                KeepAlivePingDelay = TimeSpan.FromSeconds(15),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan
            };

            //HttpClientHandler httpHandler = new HttpClientHandler();
            //httpHandler.ServerCertificateCustomValidationCallback =
            //    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            marginPledge_Channel = GrpcChannel.ForAddress(marginPledge_HostAddress,
                new GrpcChannelOptions { HttpHandler = socketHttpHandler });

            mpRpcClient = new MarginPledgeRPC.MarginPledgeRPCClient(marginPledge_Channel);

            /* Monitor */
            MonitorRPC();
        }

        ~MarginPledgeClientProcess()
        {
            ctsRPC!.Cancel();
            cts.Cancel();
            logWriter.TryComplete();
            mrWriter.TryComplete();
        }


        public async Task<IsAliveReply> IsAlive()
        {
            IsAliveReply isAlive = await mpRpcClient.IsAliveAsync(new IsAliveRequest());
            return isAlive;
        }

        public async Task Repledge(RepledgeRequestModel request)
        {
            RepledgeRequest req = new() { ReqSeqNo = request.ReqId };

            /* Get Repledge data from RPC*/
            var repledgeReply = await mpRpcClient.RepledgeAsync(req);

            if(repledgeReply!.IsSuccess)
                Repledge(repledgeReply);
        }

        public async Task<MarginPledge_ExtendedStatus> GetStatus()
        {
            MarginPledge_ExtendedStatus reply;
            try
            {
                StatusReply status = await mpRpcClient.StatusAsync(new StatusRequest());

                reply = new(status);
                //reply.Api_ProcessQueue = requestData.Count;
                //reply.Api_ReturnQueue = responseData.Count;
            }
            catch (Exception) { reply = new(); }

            //TestLog();

            return reply;
        }

        public async Task<OurFileResponseModel> GetLogFile(DateTime logDate)
        {
            OurFileResponseModel logFile = new();

            using var call = mpRpcClient.GetLogFile(new GetLogFileRequest()
            {
                LogDate = Timestamp.FromDateTimeOffset(logDate)
            });

            await foreach (GetLogFileReply logFileReply in call.ResponseStream.ReadAllAsync())
            {
                if (logFileReply.MetaInfo != null)
                {
                    logFile.FileName = logFileReply.MetaInfo.FileName;
                    logFile.FileStream = new MemoryStream();
                }
                else
                {
                    logFile.FileStream.Write(logFileReply.Content.FileChunk.ToArray());
                }
            }

            return logFile;
        }

        public async Task ProcessResponse(string responseData)
        {
            LogHttpSuccess(string.Empty, responseData);
        }

        public async Task<string> XDecrypt(string encData)
        {
            try
            {
                XRequest req = new() { Input = encData };
                var resp = await mpRpcClient.XDecryptAsync(req);
                return resp.Output;
            }
            catch (Exception) { return string.Empty; }
        }


        async Task BeginRPC()
        {
            var rpcMP = mpRpcClient.OpenStream();
            try
            {
                /* Background task to receive messages from RPC */
                var readTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (FromRPC dataFromRPC in rpcMP.ResponseStream.ReadAllAsync(ctsRPC.Token))
                        { }
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                    {
                        ctsRPC.Cancel();
                    }
                });


                /* Send messages to RPC */
                await foreach (var item in logReader.ReadAllAsync(ctsRPC.Token))
                {
                    await rpcMP.RequestStream.WriteAsync(item);
                }


                /* Disconnect RPC */
                await rpcMP.RequestStream.CompleteAsync();
                await readTask;
            }
            catch (Exception) { }
            finally
            {
                if (rpcMP is not null)
                    rpcMP.Dispose();
            }

            tskRPC = null;
        }

        async Task Log(string reqId, string message)
        {
            InitRPC();

            logWriter.TryWrite(new ToRPC()
            {
                LogData = new OneLog()
                {
                    ReqId = reqId,
                    Msg = $"{DateTime.Now.ToString(conLogTime)} : {message}"
                }
            });

            //if (tskRPC is null)
            //    tskRPC = Task.Run(() => BeginSrvc(), cts.Token);
        }
        async Task LogHttpSuccess(string reqId, string replyMessage)
        {
            InitRPC();

            logWriter.TryWrite(new ToRPC()
            {
                LogData = new()
                {
                    ReqId = reqId,
                    Msg = $"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.HttpResponse)} : {reqId} : {replyMessage}"
                }
            });

            logWriter.TryWrite(new ToRPC()
            {
                HttpResponse = new()
                {
                    ReqId = reqId,
                    Data = replyMessage
                }
            });

            //if (tskRPC is null)
            //    tskRPC = Task.Run(() => BeginSrvc(), cts.Token);
        }
        async Task LogHttpError(string reqId, string errorMessage, int ReqAttempt)
        {
            InitRPC();

            logWriter.TryWrite(new ToRPC()
            {
                HttpFailure = new()
                {
                    ReqId = reqId,
                    Msg = $"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.HttpError)} : {reqId} : Attempt {ReqAttempt} failed with error : {errorMessage}"
                }
            });

            //if (tskRPC is null)
            //    tskRPC = Task.Run(() => BeginSrvc(), cts.Token);
        }
        async Task LogHttpRetry(string reqId, string message, int reqAttempt)
        {
            InitRPC();

            logWriter.TryWrite(new ToRPC()
            {
                HttpRetry = new()
                {
                    ReqId = reqId,
                    Msg = $"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.HttpRetry)} : {reqId} : {message} : Attempt {reqAttempt}"
                }
            });

            //if (tskRPC is null)
            //    tskRPC = Task.Run(() => BeginSrvc(), cts.Token);
        }

        async Task MonitorRPC()
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ConnectivityState currentState = marginPledge_Channel.State;

                    if (currentState == ConnectivityState.Ready)
                        InitRPC();

                    await marginPledge_Channel.WaitForStateChangedAsync(currentState, cts.Token);
                }
            }
            catch { }
        }

        async Task RepledgeWorker()
        {
            try
            {
                await foreach (var item in mrReader.ReadAllAsync(cts.Token))
                {
                    await semaphore.WaitAsync(cts.Token);

                    _ = Task.Run(() => SendHttp(item));
                }
            }
            catch { }

            tskCDSL = null;
        }

        async Task SendHttp(RepledgeReply reqObj)
        {
            bool isSuccess = false;
            try
            {
                string jsonString = "{\"pledgedtls\":\"" + reqObj.Data + "\"}";

                /* Http object */
                HttpClient httpClient = _httpClientFactory.CreateClient("MarginRepledge");

                /* Log */
                if (reqObj.ReqAttempt == 1)
                {
                    var logData = $"{nameof(logType.HttpRequest)} : {reqObj.RepledgeHdrReqId} : URL : {httpClient.BaseAddress}{_cdslEndpoint} BODY : {jsonString}";
                    Log(reqObj.RepledgeHdrReqId, logData);
                }
                else
                {
                    var logData = $"URL : {httpClient.BaseAddress}{_cdslEndpoint} BODY : {jsonString}";
                    LogHttpRetry(reqObj.RepledgeHdrReqId, logData, reqObj.ReqAttempt);
                }


                /* Send Http request to CDSL */
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                content.Headers.Add("dpid", reqObj.RepledgeHdrDPID);
                content.Headers.Add("reqid", reqObj.RepledgeHdrReqId);

                var httpResponse = await httpClient.PostAsync(_cdslEndpoint, content, cts.Token);

                if (httpResponse.IsSuccessStatusCode)
                {
                    var strResponse = await httpResponse.Content.ReadAsStringAsync();

                    /* Log Http Success */
                    LogHttpSuccess(reqObj.RepledgeHdrReqId, strResponse);
                    isSuccess = true;
                }
                else
                {
                    /* Log Http Error */
                    LogHttpError(reqObj.RepledgeHdrReqId, httpResponse.StatusCode.ToString(), reqObj.ReqAttempt);
                }
            }
            catch (Exception ex)
            {
                /* Log Error */
                LogHttpError(reqObj.RepledgeHdrReqId, ex.Message, reqObj.ReqAttempt);
            }
            finally
            {
                /* Retry */
                if (!isSuccess && httpConfigTracker.IsRetryEnabled)
                    RetryHttp(reqObj.RepledgeHdrReqId, reqObj.Data, reqObj.RepledgeHdrDPID, reqObj.ReqAttempt);

                semaphore.Release();
            }
        }

        async Task TestLog()
        {
            InitRPC();

            logWriter.TryWrite(new ToRPC()
            {
                LogData = new OneLog()
                {
                    ReqId = "123",
                    Msg = $"{temp++} at {DateTime.Now.ToString()}"
                }
            });
        }


        void InitRPC()
        {
            if (tskRPC is null)
            {
                if (marginPledge_Channel.State == ConnectivityState.Ready)
                {
                    ctsRPC = new();
                    tskRPC = Task.Run(() => BeginRPC(), ctsRPC.Token);
                }
            }
            else
            {
                if (marginPledge_Channel.State != ConnectivityState.Ready)
                    ctsRPC!.Cancel();
            }
        }

        void Repledge(RepledgeReply repledgeData)
        {
            repledgeData.ReqAttempt++;
            mrWriter.TryWrite(repledgeData);

            if (tskCDSL is null)
                tskCDSL = Task.Run(() => RepledgeWorker(), cts.Token);
        }

        void RetryHttp(string reqId, string Data, string RepledgeHdrDPID, int ReqAttempt)
        {
            if (!httpConfigTracker.IsRetryEnabled) return;
            if (ReqAttempt >= httpConfigTracker.RetryCount) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    RepledgeReply repledgeData = new RepledgeReply()
                    {
                        Data = Data,
                        RepledgeHdrDPID = RepledgeHdrDPID,
                        RepledgeHdrReqId = reqId,
                        ReqAttempt = ReqAttempt
                    };

                    await Task.Delay(TimeSpan.FromSeconds(httpConfigTracker.RetryIntervalInSeconds),
                        cts.Token);

                    if (httpConfigTracker.IsRetryEnabled)
                        Repledge(repledgeData);
                }
                catch { }
            });
        }
    }
}
