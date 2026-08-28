using Google.Protobuf;
using Grpc.Core;
using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;


namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Services
{
    public class MarginPledgeService : MarginPledgeRPC.MarginPledgeRPCBase
    {
        IMarginPledgeProcess marginPledgeProcess;
        ILogManager logManager;

        public MarginPledgeService(IMarginPledgeProcess marginPledgeProcess, ILogManager logManager)
        {
            this.marginPledgeProcess = marginPledgeProcess;
            this.logManager = logManager;
        }


        public override Task<IsAliveReply> IsAlive(IsAliveRequest request, ServerCallContext context)
        {
            return Task.FromResult(new IsAliveReply
            {
                IsAlive = true
            });
        }

        public override async Task<RepledgeReply> Repledge(RepledgeRequest request, ServerCallContext context)
        {
            RepledgeReply reply = await marginPledgeProcess.Repledge(request);
            return reply;
        }

        public override async Task GetLogFile(GetLogFileRequest request,
            IServerStreamWriter<GetLogFileReply> responseStream, ServerCallContext context)
        {
            try
            {
                DateTime date = request.LogDate.ToDateTimeOffset().LocalDateTime;

                string logFileName = logManager.GetLogFileName(date);
                if (!string.IsNullOrEmpty(logFileName))
                {
                    /* File Info */
                    await responseStream.WriteAsync(new GetLogFileReply()
                    {
                        MetaInfo = new LogFileName() { FileName = Path.GetFileName(logFileName) }
                    });


                    /* File Content */
                    using (FileStream fs = new FileStream(logFileName, FileMode.Open))
                    {
                        Byte[] buffer = new Byte[4096];
                        Int32 readed = 0;

                        while ((readed = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            await responseStream.WriteAsync(new GetLogFileReply()
                            {
                                Content = new LogFileData() { FileChunk = ByteString.CopyFrom(buffer, 0, readed) }
                            });
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        public override async Task OpenStream(IAsyncStreamReader<ToRPC> requestStream,
            IServerStreamWriter<FromRPC> responseStream, ServerCallContext context)
        {
            //string clientIP = context.Peer;
            try
            {
                /* Read from client in a background task */
                var readTask = Task.Run(async () =>
                {
                    await foreach (var dataFromAPI in requestStream.ReadAllAsync())
                    {
                        switch (dataFromAPI.DataCase)
                        {
                            case ToRPC.DataOneofCase.LogData:
                                marginPledgeProcess.EnqueLogDataFromAPI(dataFromAPI.LogData);
                                break;
                            
                            case ToRPC.DataOneofCase.HttpResponse:
                                marginPledgeProcess.EnqueHttpSuccess(dataFromAPI.HttpResponse);
                                break;
                            
                            case ToRPC.DataOneofCase.HttpFailure:
                                marginPledgeProcess.EnqueHttpFailure(dataFromAPI.HttpFailure);
                                break;

                            case ToRPC.DataOneofCase.HttpRetry:
                                marginPledgeProcess.EnqueHttpRetry(dataFromAPI.HttpRetry);
                                break;

                            default:
                                break;
                        }
                    }
                });


                /* Send to client */
                while (!readTask.IsCompleted)
                {
                    //if (marginPledgeProcess.IsRequestQueued)
                    //{
                    //    while (marginPledgeProcess.GetRequestData(out FromRPC? dataToAPI))
                    //    {
                    //        if (dataToAPI is not null)
                    //        {
                    //            await responseStream.WriteAsync(dataToAPI);

                    //            //marginPledgeProcess.Distributed(dataToAPI);
                    //        }
                    //    }
                    //}

                    await Task.Delay(TimeSpan.FromSeconds(1.0));
                }
            }
            catch (Exception) { }
        }

        public override Task<StatusReply> Status(StatusRequest request, ServerCallContext context)
        {
            return Task.FromResult(marginPledgeProcess.GetStatus());
        }

        public override async Task StatusLive(StatusRequest requestStream,
            IServerStreamWriter<StatusReply> responseStream, ServerCallContext context)
        {
            try
            {
                StatusReply reply = new StatusReply();
                double delay = 3;

                while (true)
                {
                    StatusReply status = marginPledgeProcess.GetStatus();

                    if (status.Status != reply.Status
                        || status.Request != reply.Request
                        || status.HttpSuccess != reply.HttpSuccess
                        || status.HttpFailure != reply.HttpFailure
                        || status.HttpRetry != reply.HttpRetry
                        || status.ProcessError != reply.ProcessError
                        || status.DataOk != reply.DataOk
                        || status.DataError != reply.DataError)
                    {
                        await responseStream.WriteAsync(status);

                        reply = status;
                        delay = 1;
                    }
                    else if (delay != 3) delay = 3;

                    await Task.Delay(TimeSpan.FromSeconds(delay), context.CancellationToken);
                }
            }
            catch { }
        }


        public override async Task<XReply> XDecrypt(XRequest request, ServerCallContext context)
        {
            XReply reply = new();
            reply.Output = marginPledgeProcess.XDecrypt(request.Input);
            return reply;
        }
    }
}
