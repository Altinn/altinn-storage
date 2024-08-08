using System;
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
        private string _orgSafeList;

        /// <summary>
        /// List of valid ip addresses
        /// </summary>
        public string Safelist
        {
            set
            {
                _orgSafeList = value;
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
            Console.WriteLine("\r\n\r\nIp addresslist: " + ipAddressList + "\r\n\r\n");
            Console.WriteLine("\r\n\r\nSafelist: " + _orgSafeList + "\r\n\r\n");
            if (!string.IsNullOrEmpty(ipAddressList))
            {
                validIp = false;
                foreach (string ipAddress in _safeList)
                {
                    if (ipAddressList.Contains(ipAddress))
                    {
                        Console.WriteLine("\r\n\r\nValid\r\n\r\n");
                        validIp = true;
                        break;
                    }

                    Console.WriteLine("\r\n\r\nNot valid " + ipAddress + "\r\n\r\n");
                }
            }

            if (!validIp)
            {
                context.Result = new ForbidResult(ipAddressList);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
