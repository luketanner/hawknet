﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace HawkNet
{
    public static class Hawk
    {
        readonly static string[] RequiredAttributes = { "id", "ts", "mac" };
        readonly static string[] OptionalAttributes = { "ext" };
        readonly static string[] SupportedAttributes;
        readonly static string[] SupportedAlgorithms = { "HMACSHA1", "HMACSHA256" };

        static Hawk()
        {
            SupportedAttributes = RequiredAttributes.Concat(OptionalAttributes).ToArray();
        }

        public static ClaimsPrincipal Authenticate(string authorization, string host, string method, Uri uri, Func<string, HawkCredential> credentials)
        {
            if (string.IsNullOrWhiteSpace(authorization))
            {
                throw new ArgumentException("Authorization parameter can not be null or empty", "authorization");
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host header can not be null or empty", "host");
            }

            var attributes = ParseAttributes(authorization);

            if (!RequiredAttributes.All(a => attributes.AllKeys.Any(k => k == a)))
            {
                throw new SecurityException("Missing attributes");
            }

            if (!attributes.AllKeys.All(a => SupportedAttributes.Any(k => k == a)))
            {
                throw new SecurityException( "Unknown attributes");
            }

            HawkCredential credential = null;
            try
            {
                credential = credentials(attributes["id"]);
            }
            catch (Exception ex)
            {
                throw new SecurityException("Unknown user", ex);
            }

            if (credential == null)
            {
                throw new SecurityException("Missing credentials");
            }

            if (string.IsNullOrWhiteSpace(credential.Algorithm) ||
                string.IsNullOrWhiteSpace(credential.Key))
            {
                throw new SecurityException("Invalid credentials");
            }

            if (!SupportedAlgorithms.Any(a => string.Equals(a, credential.Algorithm, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new SecurityException("Unknown algorithm");
            }

            var mac = CalculateMac(host, method, uri, attributes["ext"], attributes["ts"], credential);
            if (!mac.Equals(attributes["mac"]))
            {
                throw new SecurityException("Bad mac");
            }

            var userClaim = new Claim(ClaimTypes.Name, (credential.User != null) ? credential.User : "");
            var allClaims = Enumerable.Concat(new Claim[] { userClaim }, 
                (credential.AdditionalClaims != null) ? credential.AdditionalClaims : Enumerable.Empty<Claim>());

            var identity = new ClaimsIdentity(allClaims, "Hawk");
            var principal = new ClaimsPrincipal(new ClaimsIdentity[] { identity });

            return principal;
        }

        public static string GetAuthorizationHeader(string host, string method, Uri uri, HawkCredential credential, string ext = null, DateTime? ts = null)
        {
            if(string.IsNullOrEmpty(host))
                throw new ArgumentException("The host can not be null or empty", "host");

            if (string.IsNullOrEmpty(method))
                throw new ArgumentException("The method can not be null or empty", "method");

            if(credential == null)
                throw new ArgumentNullException("The credential can not be null", "credential");

            var normalizedTs = ConvertToUnixTimestamp((ts.HasValue) ? ts.Value : DateTime.UtcNow).ToString();

            var mac = CalculateMac(host, method, uri, ext, normalizedTs, credential);

            var authParameter = string.Format("id=\"{0}\", ts=\"{1}\", mac=\"{2}\", ext=\"{3}\"",
                credential.Id, ts, mac, ext);

            return authParameter;
        }

        public static NameValueCollection ParseAttributes(string authorization)
        {
            var allAttributes = new NameValueCollection();

            foreach (var attribute in authorization.Split(','))
            {
                var index = attribute.IndexOf('=');
                if (index > 0)
                {
                    var key = attribute.Substring(0, index).Trim();
                    var value = attribute.Substring(index + 1).Trim();

                    if (value.StartsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    allAttributes.Add(key, value);
                }
            }

            return allAttributes;
        }

        public static string CalculateMac(string host, string method, Uri uri, string ext, string ts, HawkCredential credential)
        {
            var sanitizedHost = (host.IndexOf(':') > 0) ?
                host.Substring(0, host.IndexOf(':')) :
                host;

            var normalized = ts + "\n" +
                     method.ToUpper() + "\n" +
                     uri.PathAndQuery + "\n" +
                     host.ToLower() + "\n" +
                     uri.Port.ToString() + "\n" +
                     ((ext != null) ? ext : "") + "\n";

            var keyBytes = Encoding.ASCII.GetBytes(credential.Key);
            var messageBytes = Encoding.ASCII.GetBytes(normalized);

            var hmac = HMAC.Create(credential.Algorithm);
            hmac.Key = keyBytes;

            var mac = hmac.ComputeHash(messageBytes);

            return Convert.ToBase64String(mac);
        }

        public static double ConvertToUnixTimestamp(DateTime date)
        {
            var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var diff = date.ToUniversalTime() - origin;
            return Math.Floor(diff.TotalSeconds);
        }
    }
}