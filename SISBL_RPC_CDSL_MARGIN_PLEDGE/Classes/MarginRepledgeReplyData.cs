namespace SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes
{
    public class MarginRepledgeReplyData
    {
        public string reqid { get; set; }
        public string restime { get; set; }
        public string pledgeidentifier { get; set; }
        public string resstatus { get; set; }
        public string reserror { get; set; }
        public string reserrmsg { get; set; }
        public MarginRepledgeReplyDataISIN[] mrgnrepldgdtls { get; set; }
    }


    public class MarginRepledgeReplyDataISIN
    {
        public string agrmntno { get; set; }
        public string boreqstatus { get; set; }
        public string cmid { get; set; }
        public string pldgrdpintrefno { get; set; }
        public string pledgeeboid { get; set; }
        public string pledgeeboreqid { get; set; }
        public string pledgeexpdate { get; set; }
        public string pledgequantity { get; set; }
        public string pledgeremarks { get; set; }
        public string pledgeseqno { get; set; }
        public string pledgetype { get; set; }
        public string pledgevalue { get; set; }
        public string prfno { get; set; }
        public string reasoncode { get; set; }
        public string reserrmsg { get; set; }
        public string reserror { get; set; }
        public string segmentid { get; set; }
    }
}
