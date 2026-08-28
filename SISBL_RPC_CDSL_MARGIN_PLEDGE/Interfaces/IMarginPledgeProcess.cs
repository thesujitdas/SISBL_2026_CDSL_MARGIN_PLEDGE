using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces
{
    public interface IMarginPledgeProcess
    {
        public Task<RepledgeReply> Repledge(RepledgeRequest request);

        public void EnqueLogDataFromAPI(OneLog reqData);
        public void EnqueHttpSuccess(OneHttpSuccess reqData);
        public void EnqueHttpFailure(OneHttpFailure reqData);
        public void EnqueHttpRetry(OneHttpRetry reqData);
        public StatusReply GetStatus();
        public string XDecrypt(string EncryptedString);
    }
}
