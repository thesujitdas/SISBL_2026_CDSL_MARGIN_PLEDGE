namespace SISBL_API_CDSL_MARGIN_PLEDGE.Models
{
    public class MarginRepledge_ExtendedStatus
    {
        public int RepledgeRequest {  get; set; }
        public string RPC_Status { get; set; }
    }


    public class MarginRepledge_Status: MarginRepledge_ExtendedStatus
    {
        RPC_CDSL_MARGIN_REPLEDGE_V1.Protos.StatusReply _status { get; set; }

        public MarginRepledge_Status(RPC_CDSL_MARGIN_REPLEDGE_V1.Protos.StatusReply status)
        {
            this._status = status;
        }

        public string Status { get { return _status.Status; } }
        public int Requests { get { return _status.Request; } }
        public int HttpSuccess { get { return _status.HttpSuccess; } }
        public int HttpFailure { get { return _status.HttpFailure; } }
        public int HttpRetry { get { return _status.HttpRetry; } }
        public int ProcessError { get { return _status.ProcessError; } }
        public int DataOk { get { return _status.DataOk; } }
        public int DataError { get { return _status.DataError; } }
    }
}
