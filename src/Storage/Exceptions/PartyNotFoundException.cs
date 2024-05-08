using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Exceptions
{
    /// <summary>
    /// Exception class to hold exceptions when party is not found in register
    /// </summary>
    public class PartyNotFoundException : Exception
    {
        /// <summary>
        /// Responsible for holding an http request exception towards platform.
        /// </summary>
        public HttpResponseMessage Response { get; }

        /// <summary>
        /// Creates a platform exception
        /// </summary>
        /// <param name="response">The http response</param>
        /// <returns>A PartyNotFoundException</returns>
        public static async Task<PartyNotFoundException> CreateAsync(HttpResponseMessage response)
        {
            string content = await response.Content.ReadAsStringAsync();
            string message = $"{(int)response.StatusCode} - {response.ReasonPhrase} - {content}";

            return new PartyNotFoundException(response, message);
        }

        /// <summary>
        /// Copy the response for further investigations
        /// </summary>
        /// <param name="response">the response</param>
        /// <param name="message">the message</param>
        public PartyNotFoundException(HttpResponseMessage response, string message) : base(message)
        {
            this.Response = response;
        }
    }
}
