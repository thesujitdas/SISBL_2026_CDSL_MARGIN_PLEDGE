using RPC_CDSL_MARGIN_PLEDGE_V1.Protos;
using SISBL_API_CDSL_MARGIN_PLEDGE.Models;
using SISBL_COMMON.Models;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Interfaces
{
    public interface IMarginPledgeClientProcess
    {
        public Task<IsAliveReply> IsAlive();
        public Task<OurFileResponseModel> GetLogFile(DateTime logDate);
        public Task<MarginPledge_ExtendedStatus> GetStatus();
        public Task ProcessResponse(string responseData);
        public Task Repledge(RepledgeRequestModel request);
        public Task<string> XDecrypt(string encData);
    }
}
