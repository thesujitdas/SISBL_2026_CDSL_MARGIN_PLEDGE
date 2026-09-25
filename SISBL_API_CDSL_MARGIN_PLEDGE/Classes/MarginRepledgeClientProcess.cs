using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using RPC_CDSL_MARGIN_REPLEDGE_V1.Protos;
using SISBL_API_CDSL_MARGIN_PLEDGE.Interfaces;
using SISBL_API_CDSL_MARGIN_PLEDGE.Models;
using SISBL_COMMON.Models;
using System.Data;
using System.Text;
using System.Threading.Channels;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Classes
{
    public class MarginRepledgeClientProcess: IMarginRepledgeClientProcess
    {
        enum StatusOptions { Started, Stopping, Stopped };
        enum logType
        {
            HttpRequest,
            HttpResponse,
            HttpError,
            HttpRetry
        };
        const string conError = "Error ";
        const string conLogTime = "hh:mm:ss tt";
        static int temp = 1;

        IHttpClientFactory _httpClientFactory;
        HttpRequestRetryTracker httpConfigTracker;
        GrpcChannel marginRepledge_Channel;
        MarginRepledgeRPC.MarginRepledgeRPCClient mpRpcClient;

        bool _isLive = false;
        DateTime today = DateTime.Today.AddDays(-1);
        int _repledgeRequest = 0;
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


        public MarginRepledgeClientProcess(IConfiguration Configuration, IHttpClientFactory httpClientFactory,
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

            marginRepledge_Channel = GrpcChannel.ForAddress(marginPledge_HostAddress,
                new GrpcChannelOptions { HttpHandler = socketHttpHandler });

            mpRpcClient = new MarginRepledgeRPC.MarginRepledgeRPCClient(marginRepledge_Channel);

            /* Monitor */
            MonitorRPC();
        }

        ~MarginRepledgeClientProcess()
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
            InitToday();
            Interlocked.Increment(ref _repledgeRequest);
            RepledgeRequest req = new() { ReqSeqNo = request.ReqId };

            /* Get Repledge data from RPC*/
            var repledgeReply = await mpRpcClient.RepledgeAsync(req);

            if(repledgeReply!.IsSuccess)
                Repledge(repledgeReply);
        }

        public async Task<OurResponseModel> GetStatus()
        {
            InitToday();
            OurResponseModel reply = new();
            try
            {
                StatusReply status = await mpRpcClient.StatusAsync(new StatusRequest());

                MarginRepledge_Status ext = new MarginRepledge_Status(status);
                ext.RepledgeRequest = _repledgeRequest;
                ext.RPC_Status = marginRepledge_Channel.State.ToString();
                reply.Data = ext;
            }
            catch (Exception)
            {
                MarginRepledge_ExtendedStatus ext = new();
                ext.RepledgeRequest = _repledgeRequest;
                ext.RPC_Status = marginRepledge_Channel.State.ToString();
                reply.Data = ext;
            }

            reply.IsSuccess = true;
            return reply;
        }
        //public async Task<MarginRepledge_ExtendedStatus> GetStatus()
        //{
        //    MarginRepledge_ExtendedStatus reply;
        //    try
        //    {
        //        StatusReply status = await mpRpcClient.StatusAsync(new StatusRequest());

        //        reply = new(status);
        //        //reply.Api_ProcessQueue = requestData.Count;
        //        //reply.Api_ReturnQueue = responseData.Count;
        //    }
        //    catch (Exception) { reply = new(); }

        //    //TestLog();

        //    return reply;
        //}

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
                    ConnectivityState currentState = marginRepledge_Channel.State;

                    if (currentState == ConnectivityState.Ready)
                        InitRPC();

                    await marginRepledge_Channel.WaitForStateChangedAsync(currentState, cts.Token);
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

                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                content.Headers.Add("dpid", reqObj.RepledgeHdrDPID);
                content.Headers.Add("reqid", reqObj.RepledgeHdrReqId);

                /* Log */
                var dpid = content.Headers.GetValues("dpid").First();
                var reqid = content.Headers.GetValues("reqid").First();
                string version;
                if (content.Headers.TryGetValues("version", out IEnumerable<string>? values))
                    version = values.First();
                else version = "N/A";
                
                var logData = $"URL : {httpClient.BaseAddress}{_cdslEndpoint} Headers : [dpid={dpid} reqid={reqid} version={version}] Body : {jsonString}";

                if (reqObj.ReqAttempt == 1)
                {
                    logData = $"{nameof(logType.HttpRequest)} : {reqObj.RepledgeHdrReqId} : {logData}";
                    Log(reqObj.RepledgeHdrReqId, logData);
                }
                else
                {
                    //logData = $"URL : {httpClient.BaseAddress}{_cdslEndpoint} BODY : {jsonString}";
                    LogHttpRetry(reqObj.RepledgeHdrReqId, logData, reqObj.ReqAttempt);
                }


                /* Send Http request to CDSL */
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
                if (marginRepledge_Channel.State == ConnectivityState.Ready)
                {
                    ctsRPC = new();
                    tskRPC = Task.Run(() => BeginRPC(), ctsRPC.Token);
                }
            }
            else
            {
                if (marginRepledge_Channel.State != ConnectivityState.Ready)
                    ctsRPC!.Cancel();
            }
        }

        void InitToday()
        {
            if (today.Day != DateTime.Today.Day)
            {
                today = DateTime.Today;

                _repledgeRequest = 0;
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
