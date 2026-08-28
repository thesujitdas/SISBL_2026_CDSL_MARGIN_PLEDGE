namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces
{
    public interface ILogManager
    {
        public void Clear();
        public void Log(string LogData);

        public string GetLogFileName(DateTime forDate);
    }
}
