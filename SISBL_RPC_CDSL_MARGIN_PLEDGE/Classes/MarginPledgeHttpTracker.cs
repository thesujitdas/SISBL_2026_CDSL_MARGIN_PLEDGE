using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;
using System.Collections.Concurrent;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class MarginPledgeHttpTracker: IMarginPledgeHttpTracker
    {
        Action<DataReply>? mainProcess;
        ConcurrentDictionary<string, DataReply> epReqRegister = new();

        public void Clear() => epReqRegister.Clear();

        public void SetMainProcess(Action<DataReply> processor) =>
            this.mainProcess = processor;

        public async Task Register(DataReply data)
        {
            data.ReqAttempt++;
            epReqRegister.TryAdd(data.ReqSeqNo, data);
        }

        public async Task Retry(string ReqSeqNo, int RetryCount, double RetryIntervalInSeconds)
        {
            epReqRegister.TryGetValue(ReqSeqNo, out DataReply? data);

            if (data is null)
                return;

            if (data.ReqAttempt < RetryCount)
            {
                data.ReqAttempt++;

                await Task.Delay(TimeSpan.FromSeconds(RetryIntervalInSeconds));

                mainProcess?.Invoke(data);

                //_ = Task.Run(async () =>
                //{
                //    await Task.Delay(TimeSpan.FromSeconds(RetryIntervalInSeconds));

                //    mainProcess?.Invoke(data);
                //});
            }
            else epReqRegister.TryRemove(ReqSeqNo, out _);
        }

        public async Task UnRegister(string ReqSeqNo) =>
            epReqRegister.TryRemove(ReqSeqNo, out _);
    }
}
