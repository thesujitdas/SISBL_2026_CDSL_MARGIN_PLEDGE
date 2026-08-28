using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class LogManager: ILogManager
    {
        //static IConfiguration Configuration;
        bool logEnabled = false;
        string? logFolder = string.Empty;
        string? logFileNamePattern = string.Empty;

        DateTime today = DateTime.Today.AddDays(-1);
        const string logFolderDateFormat = "dd-MM-yyyy";
        //const string logFileNamePattern = "EarlyPayInLog_{0}.txt";
        const string logFileNameDateFormat = "ddMMyyyy";
        const string logTime = "hh:mm:ss tt";
        string logPath = string.Empty;
        string logFileName = string.Empty;
        bool logWriting = false;

        Queue<string> logQue = new Queue<string>();
        StreamWriter sw = null;


        public LogManager(IConfiguration Configuration)
        {
            var sisblConfig = Configuration.GetSection("SISBL");

            logEnabled = (sisblConfig.GetValue<string>("Log_Enabled") == "1");
            logFolder = sisblConfig.GetValue<string>("Log_Folder");
            logFileNamePattern = sisblConfig.GetValue<string>("Log_FileName");
        }

        public void Clear() => logQue.Clear();

        public void Log(string LogData)
        {
            if (!logEnabled) return;

            /* Queue */
            logQue.Enqueue(LogData);
            //logQue.Enqueue($"{DateTime.Now.ToString(logTime)} : {LogData}");

            if (!logWriting)
                Task.Run(() => WriteInFile());
        }

        public string GetLogFileName(DateTime forDate)
        {
            string logPath = $"{logFolder}\\{forDate.ToString(logFolderDateFormat)}";
            string logFileName = $"{logPath}\\{String.Format(logFileNamePattern, forDate.ToString(logFileNameDateFormat))}";

            if (File.Exists(logFileName))
                return logFileName;
            else return string.Empty;
        }


        async Task WriteInFile()
        {
            logWriting = true;

            /* Log Date */
            if (today.Day != DateTime.Today.Day)
            {
                today = DateTime.Today;

                logPath = $"{logFolder}\\{today.ToString(logFolderDateFormat)}";
                logFileName = $"{logPath}\\{String.Format(logFileNamePattern, today.ToString(logFileNameDateFormat))}";
            }

            /* Create Log Folder if not Exists*/
            if (!Directory.Exists(logPath))
                Directory.CreateDirectory(logPath);

            /* Create Log File if not Exists */
            if (!File.Exists(logFileName))
                File.CreateText(logFileName).Close();

            /* Write in file */
            string? logData;
            sw = new StreamWriter(logFileName, true);

            while (logQue.Count > 0)
            {
                while (logQue.TryDequeue(out logData))
                {
                    await sw.WriteLineAsync(logData + Environment.NewLine);
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            sw.Close();
            logWriting = false;
        }
    }
}
