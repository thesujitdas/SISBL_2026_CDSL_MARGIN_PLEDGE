namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class HttpRequestRetryTracker
    {
        public const string OurTracker = "SISBL:HttpRequestRetryTracker";

        public bool IsRetryEnabled { get; set; }
        public int RetryCount { get; set; }
        public int RetryIntervalInSeconds { get; set; }
    }
}
