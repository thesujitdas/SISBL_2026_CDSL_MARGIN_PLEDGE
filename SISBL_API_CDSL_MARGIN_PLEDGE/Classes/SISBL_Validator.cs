using Grpc.Net.Client;
using SISBL_SRVC_V1.Protos;
using System.Text;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Classes
{
    public class SISBL_Validator
    {
        const char conComma = ',';
        public enum ValidationStatus { Ok, BadData, Forbiden, UnAuthorized }

        private GrpcChannel rpcChannel;

        public SISBL_Validator(IConfiguration configuration)
        {
            /* RPC Setup */
            string hostAddress;
            if (configuration.GetValue<string>("SISBL:IsLIVE") == "1")
                hostAddress = configuration.GetValue<string>("SISBL:RPC_Shrinova_LIVE");
            else hostAddress = configuration.GetValue<string>("SISBL:RPC_Shrinova_UAT");

            var socketHttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true,
            };

            rpcChannel = GrpcChannel.ForAddress(hostAddress,
                new GrpcChannelOptions { HttpHandler = socketHttpHandler });
        }

        public async Task<ValidationStatus> IsValid(string Token, string Data)
        {
            try
            {
                string tokenData = Encoding.UTF8.GetString(Convert.FromBase64String(Token));
                if (string.IsNullOrEmpty(tokenData))
                    return ValidationStatus.BadData;

                /* Process Token */
                ReadOnlySpan<char> span = tokenData.AsSpan();

                int len = span.IndexOf(conComma);
                if (len == -1)
                    return ValidationStatus.BadData;

                int pos = 0;
                int UserId = int.Parse(span.Slice(pos, len));

                string UserToken;
                pos += len + 1;
                len = span.Slice(pos).IndexOf(conComma);
                if (len == -1)
                    UserToken = span.Slice(pos).ToString();
                else UserToken = span.Slice(pos, len).ToString();

                /* Process Data */
                int UserIdInData = GetUserId(Data);
                if (UserIdInData == 0)
                    return ValidationStatus.BadData;

                /* Validate */
                if (UserIdInData != UserId)
                    return ValidationStatus.Forbiden;

                var isValid = await Validate(UserId, UserToken);
                if (!isValid)
                    return ValidationStatus.UnAuthorized;

                /* Validation success */
                return ValidationStatus.Ok;
            }
            catch (Exception) { }

            return ValidationStatus.BadData;
        }


        public async Task<bool> Validate(int UserId, string Token)
        {
            try
            {
                var client = new Account_RPC.Account_RPCClient(rpcChannel);
                var reply = await client.ValidateTokenAsync(new ValidateTokenRequest()
                {
                    UserId = UserId,
                    Token = Token
                });
                return reply.IsSuccess;
            }
            catch (Exception) { }

            return false;
        }


        int GetUserId(string Data)
        {
            if (string.IsNullOrEmpty(Data))
                return 0;

            ReadOnlySpan<char> span = Data.AsSpan();

            int len = span.IndexOf(conComma);
            if (len == -1)
                return int.Parse(span.Slice(0));
            else return int.Parse(span.Slice(0, len));
        }
    }
}
