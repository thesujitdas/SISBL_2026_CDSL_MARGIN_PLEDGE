using Microsoft.Data.SqlClient;
using RPC_CDSL_MARGIN_REPLEDGE_V1.Protos;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;
using System.Data;
using System.Xml.Linq;

namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class DB
    {
        const string conAPIS_ISB_LIVE = @"SERVER=172.16.0.111\OtherDB;DATABASE=APIS_ISB;UID=sa;PWD=otherdb123;Persist Security Info=True;TrustServerCertificate=True;";
        const string conAPIS_ISB_UAT = @"SERVER=172.30.75.230\OtherDB;DATABASE=APIS_ISB;UID=uatLikeSa;PWD=ForDev@230;Persist Security Info=True;TrustServerCertificate=True;";

        const string spGetMarginRepledgeRequestData = "DP_Stp_Get_MarginRepledgeRequestData";
        const string spMarginRepledgeResponseHeader = "DP_STP_INSERT_MarginRePledge_ResponseHeaderDetails";
        const string spMarginRepledgeResponseISIN = "DP_STP_INSERT_MarginRePledge_ResponseISINDetails";

        static SqlCommand cmdMarginRepledgeResponseHeader;
        static SqlCommand cmdMarginRepledgeResponseISIN;
        static bool isLive = false;
        static string conApisISB;

        public DB(bool isLive)
        {
            DB.isLive = isLive;

            if (DB.isLive)
                conApisISB = conAPIS_ISB_LIVE;
            else conApisISB = conAPIS_ISB_UAT;

            /* Margin Repledge Response Header */
            cmdMarginRepledgeResponseHeader = new SqlCommand(spMarginRepledgeResponseHeader, new SqlConnection(conApisISB));
            cmdMarginRepledgeResponseHeader.CommandType = CommandType.StoredProcedure;
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@reqid", DBNull.Value);
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@restime", DBNull.Value);
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@pledgeidentifier", DBNull.Value);
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@reqstatus", DBNull.Value);
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@reserror", DBNull.Value);
            cmdMarginRepledgeResponseHeader.Parameters.AddWithValue("@reserrmsg", DBNull.Value);

            /* Margin Repledge Response ISIN */
            cmdMarginRepledgeResponseISIN = new SqlCommand(spMarginRepledgeResponseISIN, new SqlConnection(conApisISB));
            cmdMarginRepledgeResponseISIN.CommandType = CommandType.StoredProcedure;
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@reqid", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@agrmntno", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@boreqstatus", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@cmid", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pldgrdpintrefno", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgeeboid", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgeexpdate", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgequantity", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgeremarks", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgeseqno", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgetype", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@pledgevalue", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@prfno", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@reasoncode", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@reserrrmsg", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@reserror", DBNull.Value);
            cmdMarginRepledgeResponseISIN.Parameters.AddWithValue("@segmentid", DBNull.Value);
        }


        public async Task<RepledgeRequestPacket> GetRepledge(string reqId)
        {
            RepledgeRequestPacket reply = new();
            reply.Data = new();

            var sqlMarginRepledgeRequest = new SqlCommand(spGetMarginRepledgeRequestData,
                new SqlConnection(conApisISB));
            try
            {
                sqlMarginRepledgeRequest.CommandType = CommandType.StoredProcedure;
                sqlMarginRepledgeRequest.Parameters.AddWithValue("@reqId", reqId);

                await sqlMarginRepledgeRequest.Connection.OpenAsync();

                using (SqlDataReader sqlDr = await sqlMarginRepledgeRequest.ExecuteReaderAsync())
                {
                    if (sqlDr.HasRows)
                    {
                        bool isFirstRow = true;
                        while (sqlDr.Read())
                        {
                            if (isFirstRow)
                            {
                                reply.RepledgeHdrDPID = sqlDr["dpid"].ToString();
                                reply.RepledgeHdrReqId = sqlDr["reqid"].ToString();

                                reply.Data.Pledgeidentifier = sqlDr["pledgeidentifier"].ToString();
                                reply.Data.Reqtime = sqlDr["reqtime"].ToString();
                                reply.Data.Executiondate = sqlDr["executiondate"].ToString();
                                isFirstRow = false;
                            }

                            reply.Data.Setupdtls.Add(new RepledgeRequestDataISIN()
                            {
                                Pledgeeboreqid = sqlDr["pledgeeboreqid"].ToString(),
                                Pledgeeboid = sqlDr["pledgeeboid"].ToString(),
                                Pledgeseqno = sqlDr["pledgeseqno"].ToString(),
                                Prfno = sqlDr["prfno"].ToString(),
                                Pledgetype = sqlDr["pledgetype"].ToString(),
                                Pledgequantity = sqlDr["pledgequantity"].ToString(),
                                Pledgeexpdate = sqlDr["pledgeexpdate"].ToString(),
                                Pledgevalue = sqlDr["pledgevalue"].ToString(),
                                Agrmntno = sqlDr["agrmntno"].ToString(),
                                Pldgrdpintrefno = sqlDr["pldgrdpintrefno"].ToString(),
                                Pledgeremarks = sqlDr["pledgeremarks"].ToString(),
                                Reasoncode = sqlDr["reasoncode"].ToString(),
                                Cmid = sqlDr["cmid"].ToString()
                            });
                        }
                        reply.IsSuccess = true;
                    }
                    else reply.Message = "No data";
                }
            }
            catch (Exception ex) { reply.Message = ex.Message; }
            finally
            {
                if (sqlMarginRepledgeRequest?.Connection.State != ConnectionState.Closed)
                    sqlMarginRepledgeRequest.Connection.Close();
            }

            return reply;
        }

        public async Task<string> UpdateResponse(MarginRepledgeReplyData data, bool hasMore)
        {
            string reply = string.Empty;

            try
            {
                /* Open DB connection if it is not open */
                if (cmdMarginRepledgeResponseHeader.Connection.State != ConnectionState.Open)
                    await cmdMarginRepledgeResponseHeader.Connection.OpenAsync();

                if (cmdMarginRepledgeResponseISIN.Connection.State != ConnectionState.Open)
                    await cmdMarginRepledgeResponseISIN.Connection.OpenAsync();

                /* Update DB */
                cmdMarginRepledgeResponseHeader.Parameters["@reqid"].Value = data.reqid;
                cmdMarginRepledgeResponseHeader.Parameters["@restime"].Value = data.restime;
                cmdMarginRepledgeResponseHeader.Parameters["@pledgeidentifier"].Value = data.pledgeidentifier;
                cmdMarginRepledgeResponseHeader.Parameters["@reqstatus"].Value = data.resstatus;
                cmdMarginRepledgeResponseHeader.Parameters["@reserror"].Value = data.reserror;
                cmdMarginRepledgeResponseHeader.Parameters["@reserrmsg"].Value = data.reserrmsg;
                cmdMarginRepledgeResponseHeader.ExecuteNonQuery();

                foreach (var item in data.mrgnrepldgdtls)
                {
                    cmdMarginRepledgeResponseISIN.Parameters["@reqid"].Value = item.pledgeeboreqid;
                    cmdMarginRepledgeResponseISIN.Parameters["@agrmntno"].Value = item.agrmntno;
                    cmdMarginRepledgeResponseISIN.Parameters["@boreqstatus"].Value = item.boreqstatus;
                    cmdMarginRepledgeResponseISIN.Parameters["@cmid"].Value = item.cmid;
                    cmdMarginRepledgeResponseISIN.Parameters["@pldgrdpintrefno"].Value = item.pldgrdpintrefno;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgeeboid"].Value = item.pledgeeboid;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgeexpdate"].Value = item.pledgeexpdate;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgequantity"].Value = item.pledgequantity;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgeremarks"].Value = item.pledgeremarks;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgeseqno"].Value = item.pledgeseqno;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgetype"].Value = item.pledgetype;
                    cmdMarginRepledgeResponseISIN.Parameters["@pledgevalue"].Value = item.pledgevalue;
                    cmdMarginRepledgeResponseISIN.Parameters["@prfno"].Value = item.prfno;
                    cmdMarginRepledgeResponseISIN.Parameters["@reasoncode"].Value = item.reasoncode;
                    cmdMarginRepledgeResponseISIN.Parameters["@reserrrmsg"].Value = item.reserrmsg;
                    cmdMarginRepledgeResponseISIN.Parameters["@reserror"].Value = item.reserror;
                    cmdMarginRepledgeResponseISIN.Parameters["@segmentid"].Value = item.segmentid;
                    cmdMarginRepledgeResponseISIN.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { reply = ex.Message; }
            finally
            {
                if (!hasMore)
                {
                    /* Close DB connection if it is open */
                    if (cmdMarginRepledgeResponseHeader.Connection.State == ConnectionState.Open)
                        await cmdMarginRepledgeResponseHeader.Connection.CloseAsync();

                    if (cmdMarginRepledgeResponseISIN.Connection.State == ConnectionState.Open)
                        await cmdMarginRepledgeResponseISIN.Connection.CloseAsync();
                }
            }

            return reply;
        }
    }
}
