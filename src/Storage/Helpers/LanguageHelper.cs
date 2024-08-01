using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// Helper methods for text.
    /// </summary>
    public static class LanguageHelper
    {
        /// <summary>
        /// Checks if a string contains two letters as expected by the ISO 3166 language code
        /// </summary>
        /// <param name="language">the language to check</param>
        /// <returns>true if it is valid, false otherwise</returns>
        public static bool IsTwoLetters(string language)
        {
            if (language == null)
            {
                return false;
            }

            return Regex.IsMatch(language, "^[A-Za-z]{2}$");
        }

        /// <summary>
        /// Get the current user's language from cookie or default nb (if
        /// not invoked from browser)
        /// </summary>
        /// <param name="request">The current http request</param>
        /// <returns>Language code</returns>
        public static string GetCurrentUserLanguage(HttpRequest request)
        {
            string cookieValue = request.Cookies["altinnPersistentContext"];
            if (string.IsNullOrEmpty(cookieValue))
            {
                return "nb";
            }
            else if (cookieValue.Contains("UL=1033"))
            {
                return "en";
            }
            else if (cookieValue.Contains("UL=1044"))
            {
                return "nb";
            }
            else if (cookieValue.Contains("UL=2068"))
            {
                return "nn";
            }

            return "nb";
        }
    }
}
