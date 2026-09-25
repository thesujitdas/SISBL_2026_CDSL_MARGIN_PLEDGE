using RPC_CDSL_MARGIN_REPLEDGE_V1.Protos;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Linq;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class MarginRepledgeProcess: IMarginRepledgeProcess
    {
        enum StatusOptions { Started, Stopping, Stopped };
        enum logType
        {
            EarlyPayInData,
            Error,
            Requeue,
            ToGateway,
            HttpResponse,
            HttpError,
            RequestData,
            RequestDataError,
            RequestProcessError,
            Response,
            ResponseError,
            ResponseDecryptionError,
            ResponseData,
            ResponseDataError,
            ResponseDataSerializationError,
            ResponseDataCodeError,
            ResponseDataDBError,
            Callback
        };
        const string conLogTime = "hh:mm:ss tt";
        const string conFail = "1";

        DB db;
        ILogManager logManager;

        bool _isLive = false;
        string _encryptionKey = string.Empty;
        JsonSerializerOptions _serializerOptions;

        static Channel<OneHttpSuccess> channelHttpReply = Channel.CreateUnbounded<OneHttpSuccess>();
        ChannelWriter<OneHttpSuccess> httpReplyWriter = channelHttpReply.Writer;
        ChannelReader<OneHttpSuccess> httpReplyReader = channelHttpReply.Reader;
        CancellationTokenSource? cts = new();
        Task? tskHttpReply;

        DateTime today = DateTime.Today.AddDays(-1);
        int requests, httpSuccess, httpFailed, httpRetry, processError, dataError, dataOk;
        string _status = "Started";
        public string status { get { return _status; } set { _status = value; } }


        public MarginRepledgeProcess(IConfiguration Configuration, ILogManager LogManager)
        {
            var sisblConfig = Configuration.GetSection("SISBL");

            _isLive = (sisblConfig.GetValue<string>("IsLIVE") == "1");
            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
                WriteIndented = false
            };

            if (_isLive)
                _encryptionKey = sisblConfig.GetValue<string>("CDSL_MarginRepledge_EncryptionKey_LIVE");
            else _encryptionKey = sisblConfig.GetValue<string>("CDSL_MarginRepledge_EncryptionKey_UAT");

            db = new DB(_isLive);
            logManager = LogManager;
        }


        public void EnqueLogDataFromAPI(OneLog reqData)
        {
            InitToday();
            logManager.Log(reqData.Msg);
        }

        public void EnqueHttpSuccess(OneHttpSuccess reqData)
        {
            InitToday();

            httpReplyWriter.TryWrite(reqData);
            Interlocked.Increment(ref httpSuccess);

            if (tskHttpReply is null)
                tskHttpReply = Task.Run(() => ProcessHttpReply(), cts.Token);
        }

        public void EnqueHttpFailure(OneHttpFailure reqData)
        {
            InitToday();
            Interlocked.Increment(ref httpFailed);

            /* Log http error */
            logManager.Log(reqData.Msg);
        }

        public void EnqueHttpRetry(OneHttpRetry reqData)
        {
            InitToday();
            Interlocked.Increment(ref httpRetry);

            /* Log http retry */
            logManager.Log(reqData.Msg);
        }

        public StatusReply GetStatus()
        {
            InitToday();

            StatusReply reply = new StatusReply();
            reply.Status = _status;
            reply.Request = requests;
            reply.HttpSuccess = httpSuccess;
            reply.HttpFailure = httpFailed;
            reply.HttpRetry = httpRetry;
            reply.ProcessError = processError;
            reply.DataError = dataError;
            reply.DataOk = dataOk;
            return reply;
        }

        public async Task<RepledgeReply> Repledge(RepledgeRequest request)
        {
            InitToday();
            RepledgeReply reply = new();

            var data = await db.GetRepledge(request.ReqSeqNo);
            if (data.IsSuccess && !string.IsNullOrEmpty(data.RepledgeHdrReqId))
            {
                try
                {
                    string serData = JsonSerializer.Serialize(data.Data, _serializerOptions);
                    string encData = Encrypt(serData, _encryptionKey);
                    reply.Data = encData;
                    reply.RepledgeHdrDPID = data.RepledgeHdrDPID;
                    reply.RepledgeHdrReqId = data.RepledgeHdrReqId;
                    reply.IsSuccess = true;

                    Interlocked.Increment(ref requests);

                    /* Log */
                    logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.RequestData)} : {data.RepledgeHdrReqId} : {serData}");
                }
                catch (Exception ex)
                {
                    /* Log Error */
                    logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.RequestProcessError)} : {request.ReqSeqNo} : {ex.Message}");
                }
            }
            else
            {
                /* Log Error */
                logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.RequestDataError)} : {request.ReqSeqNo} : {data.Message}");
            }

            return reply;
        }

        public string XDecrypt(string EncryptedString)
        {
            return Decrypt(EncryptedString, _encryptionKey);
        }


        string Decrypt(string strData, String key)
        {
            string decoded;
            try
            {
                byte[] rawBytes = Convert.FromBase64String(strData);
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.IV = new byte[16];
                    byte[] passBytes = Encoding.UTF8.GetBytes(key);
                    aesAlg.Key = passBytes;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;

                    ICryptoTransform decryptor = aesAlg.CreateDecryptor();

                    using (MemoryStream msDecrypt = new MemoryStream(rawBytes))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        decoded = srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception) { decoded = string.Empty; }

            return decoded;
        }

        string Encrypt(string strData, string encryptionKey)
        {
            byte[] encBytes;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.IV = new byte[16];
                byte[] passBytes = Encoding.UTF8.GetBytes(encryptionKey);
                aesAlg.Key = passBytes;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor();

                using (MemoryStream msEncrypt = new MemoryStream())
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(strData);
                    }
                    encBytes = msEncrypt.ToArray();
                }
            }

            string encoded = Convert.ToBase64String(encBytes);

            return encoded;
        }

        void InitToday()
        {
            if (today.Day != DateTime.Today.Day)
            {
                today = DateTime.Today;

                logManager.Clear();

                requests = httpSuccess = httpFailed = httpRetry = processError = dataError = dataOk = 0;
            }
        }

        async Task ProcessHttpReply()
        {
            do
            {
                try
                {
                    await foreach (var item in httpReplyReader.ReadAllAsync(cts.Token))
                    {
                        try
                        {
                            var decrypted = Decrypt(item.Data, _encryptionKey);

                            if (string.IsNullOrEmpty(decrypted))
                            {
                                logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.ResponseDecryptionError)} : {item.ReqId}");
                                continue;
                            }

                            /* Log decrypted data */
                            logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.ResponseData)} : {item.ReqId} : {decrypted}");


                            /* Data object */
                            MarginRepledgeReplyData? mrObj =
                                JsonSerializer.Deserialize<MarginRepledgeReplyData>(decrypted);

                            if (mrObj is null)
                            {
                                /* Log response serialization error */
                                logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.ResponseDataSerializationError)} : {item.ReqId}");

                                Interlocked.Increment(ref processError);
                                continue;
                            }

                            /* Check Ok/Error */
                            if (mrObj.resstatus == conFail
                                || mrObj.mrgnrepldgdtls.Any(i => i.boreqstatus == conFail))
                            {
                                Interlocked.Increment(ref dataError);
                            }
                            else Interlocked.Increment(ref dataOk);


                            /* DB */
                            var dbMessage = await db.UpdateResponse(mrObj, httpReplyReader.TryPeek(out _));

                            if (!string.IsNullOrEmpty(dbMessage))
                            {
                                Interlocked.Increment(ref processError);

                                /* Log DB error */
                                logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.ResponseDataDBError)} : {item.ReqId} : {dbMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.ResponseError)} : {item.ReqId} : {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logManager.Log($"{DateTime.Now.ToString(conLogTime)} : {nameof(logType.Error)} : {ex.Message}");
                }
            }
            while (!cts.IsCancellationRequested && httpReplyReader.TryPeek(out _));

            tskHttpReply = null;
        }
    }
}
