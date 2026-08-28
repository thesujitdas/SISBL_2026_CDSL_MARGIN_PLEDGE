namespace SISBL_API_CDSL_MARGIN_PLEDGE.Models
{
    public class MarginPledge_ExtendedStatus
    {
        RPC_CDSL_MARGIN_PLEDGE_V1.Protos.StatusReply _status { get; set; }

        public MarginPledge_ExtendedStatus()
        {
            this._status = new();
        }
        public MarginPledge_ExtendedStatus(RPC_CDSL_MARGIN_PLEDGE_V1.Protos.StatusReply status)
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
