using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPC_CDSL_MARGIN_REPLEDGE_V1.Protos;
using SISBL_API_CDSL_MARGIN_PLEDGE.Classes;
using SISBL_API_CDSL_MARGIN_PLEDGE.Interfaces;
using SISBL_API_CDSL_MARGIN_PLEDGE.Models;
using SISBL_COMMON.Models;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [TypeFilter(typeof(SISBL_Authorize))]
    public class MRController : ControllerBase
    {
        IMarginRepledgeClientProcess marginPledgeProcess;

        public MRController(IMarginRepledgeClientProcess marginPledgeProcess)
        {
            this.marginPledgeProcess = marginPledgeProcess;
        }
        //
        // Accessed by SISBL Service Monitor
        //
        [HttpPost("isAlive")]
        public async Task<ActionResult<OurResponseModel>> IsAlive([FromBody] string value)
        {
            OurResponseModel resp = new();
            try
            {
                /* Process */
                IsAliveReply reply = await marginPledgeProcess.IsAlive();
                resp.Data = reply;
                resp.IsSuccess = true;
            }
            catch (Exception ex) { resp.Message = ex.Message; }

            return Ok(resp);
        }


        [HttpPost("status")]
        //[AllowAnonymous]
        public async Task<ActionResult<OurResponseModel>> Status([FromBody] string value)
        {
            OurResponseModel resp = await marginPledgeProcess.GetStatus();
            return Ok(resp);
        }


        [HttpPost("log")]
        public async Task<IActionResult> DownloadFile([FromBody] string value)
        {
            const string dateFormat = "dd-MM-yyyy";

            try
            {
                /* Request-Body */
                OurPostedUserModel reqData = new(value);

                /* Process */
                DateTime logDate = DateTime.Now;

                if (string.IsNullOrEmpty(reqData.Data))
                {
                    logDate = DateTime.Now;
                }
                else
                {
                    /* dd?MM?yyyy */
                    if (reqData.Data.Length != 10) return BadRequest();

                    int[] dates = Array.ConvertAll(reqData.Data.Split(reqData.Data.Substring(2, 1)), int.Parse);
                    if (dates.Length != 3) return BadRequest();

                    logDate = new DateTime(dates[2], dates[1], dates[0]);
                }


                /* Get log */
                var logFile = await marginPledgeProcess.GetLogFile(logDate);

                if (!string.IsNullOrEmpty(logFile.FileName))
                {
                    /* Return file */
                    logFile.FileStream.Position = 0;

                    return File(logFile.FileStream, contentType: "text/plain",
                        fileDownloadName: logFile.FileName/*, enableRangeProcessing: true*/);
                }
                else
                {
                    /* Not found */
                    OurResponseModel resp = new();
                    resp.Message = $"No log found for {logDate.ToString(dateFormat)}";
                    return Ok(resp);
                }
            }
            catch (Exception) { }

            return BadRequest();
        }


        /* Process encrypted response */
        //[HttpPost("processResponse")]
        //[AllowAnonymous]
        //public async Task<ActionResult> ProcessResponse([FromBody] string value)
        //{
        //    OurPostedUserModel reqData = new(value);
        //    await marginPledgeProcess.ProcessResponse(reqData.Data);

        //    return Ok();
        //}


        /* Decrypt encrypted data */
        [HttpPost("xdecrypt")]
        [AllowAnonymous]
        public async Task<ActionResult> XDecrypt([FromBody] string value)
        {
            OurPostedUserModel reqData = new(value);
            var resp = await marginPledgeProcess.XDecrypt(reqData.Data);
            return Ok(resp);
        }
        //
        // Accessed by MyAccount
        //
        [HttpPost("repledge")]
        [AllowAnonymous]
        public async Task<ActionResult> Repledge([FromBody] RepledgeRequestModel model)
        {
            /* Validation */
            if (string.IsNullOrEmpty(model.ReqId))
                return BadRequest();

            /* Initiate Repledge */
            marginPledgeProcess.Repledge(model);

            return Ok();
        }
    }
}
