using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces
{
    public interface IMarginPledgeHttpTracker
    {
        public void Clear();
        //public void SetMainProcess(Action<DataReply> processor);
        //public Task Register(DataReply data);
        public Task Retry(string ReqSeqNo, int RetryCount, double RetryIntervalInSeconds);
        public Task UnRegister(string ReqSeqNo);
    }
}
