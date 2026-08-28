using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text;

namespace SISBL_API_CDSL_MARGIN_PLEDGE.Classes
{
    public class SISBL_Authorize(SISBL_Validator Validator) : IAsyncAuthorizationFilter
    {
        private SISBL_Validator validator = Validator;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            /* Bypass Anonymous */
            if (context.ActionDescriptor.EndpointMetadata.Any(m => m is IAllowAnonymous))
                return;

            /* Process Header */
            var authHeader = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader is null || !authHeader.StartsWith("Bearer "))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            /* Process Token */
            string token = authHeader.Substring("Bearer ".Length);
            if (string.IsNullOrEmpty(token))
            {
                context.Result = new BadRequestResult();
                return;
            }

            /* Process Request-Body */
            if (context.HttpContext.Request.ContentLength == 0)
            {
                context.Result = new BadRequestResult();
                return;
            }
            string requestBody;

            context.HttpContext.Request.EnableBuffering();
            using (var reader = new StreamReader(context.HttpContext.Request.Body,
                Encoding.UTF8, true, 1024, true))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            context.HttpContext.Request.Body.Position = 0;

            requestBody = requestBody.Replace("\"", string.Empty);
            if (string.IsNullOrEmpty(requestBody))
            {
                context.Result = new BadRequestResult();
                return;
            }

            /* Validate */
            var validationStatus = await validator.IsValid(token, requestBody);
            if (validationStatus != SISBL_Validator.ValidationStatus.Ok)
            {
                if (validationStatus == SISBL_Validator.ValidationStatus.BadData)
                    context.Result = new BadRequestResult();
                else if (validationStatus == SISBL_Validator.ValidationStatus.Forbiden)
                    context.Result = new ForbidResult();
                else if (validationStatus == SISBL_Validator.ValidationStatus.UnAuthorized)
                    context.Result = new UnauthorizedResult();
            }
        }
    }
}
