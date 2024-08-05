using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Altinn.Platform.Storage.Filters
{
    /// <summary>
    /// ClientIpCheckActionFilter
    /// </summary>
    public class ClientIpCheckActionFilterAttribute : ActionFilterAttribute
    {
        private string[] _safeList;

        /// <summary>
        /// List of valid ip addresses
        /// </summary>
        public string Safelist
        {
            set
            {
                _safeList = value.Split(';');
            }
        }

        /// <summary>
        /// Authorize from ip address
        /// </summary>
        /// <param name="context">context</param>
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            string ipAddressList = context.HttpContext?.Request.Headers["X-Forwarded-For"].ToString();
            bool validIp = true;
            if (!string.IsNullOrEmpty(ipAddressList))
            {
                validIp = false;
                foreach (string ipAddress in _safeList)
                {
                    if (ipAddressList.Contains(ipAddress))
                    {
                        validIp = true;
                        break;
                    }
                }
            }

            if (!validIp)
            {
                context.Result = new ForbidResult();
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
