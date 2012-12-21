using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Threading;
using System.Web.Configuration;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace PointRight.WebAPIs
{
    public class WebApiOutputCacheAttribute : ActionFilterAttribute
    {
        #region Fields

        // cache length in seconds
        private int _timespan = 0;

        // client cache length in seconds
        private int _clientTimeSpan = 0;

        // cache for anonymous users only?
        private bool _anonymousOnly = false;

        // cache key
        private string _cachekey = string.Empty;

        // assigned OutputCacheProfile
        private OutputCacheProfile _outputCacheProfile;

        // cache repository
        private static readonly ObjectCache WebApiCache = MemoryCache.Default;

        // outputCacheSettings section in the Web.config
        private static readonly OutputCacheSettingsSection CacheSettingsSection
            = WebConfigurationManager.GetSection("system.web/caching/outputCacheSettings") as OutputCacheSettingsSection;

        Func<int, HttpActionContext, bool, bool> _isCachingTimeValid = (timespan, ac, anonymous) =>
        {
            if (timespan > 0)
            {
                if (anonymous)
                    if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                        return false;

                if (ac.Request.Method == HttpMethod.Get) return true;
            }

            return false;
        };

        #endregion

        #region Properties

        public bool AnonymousOnly
        {
            get { return _anonymousOnly; }
            set { _anonymousOnly = value; }
        }

        public string CacheProfile
        {
            get { return _outputCacheProfile == null ? null : _outputCacheProfile.Name; }
            set { SetOutputCacheProfile(value); }
        }

        public int ClientTimeSpan
        {
            get { return _clientTimeSpan; }
            set { _clientTimeSpan = value; }
        }

        public int Duration
        {
            get { return _timespan; }
            set { _timespan = value; }
        }

        public bool Enabled
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public bool DisableInDebugMode { get; set; }

        #endregion

        #region Private Methods

        private CacheControlHeaderValue SetClientCache()
        {
            var cachecontrol = new CacheControlHeaderValue();
            cachecontrol.MaxAge = TimeSpan.FromSeconds(_clientTimeSpan);
            cachecontrol.MustRevalidate = true;
            return cachecontrol;
        }

        private void SetOutputCacheProfile(string cacheProfileName)
        {
            if (_outputCacheProfile != null && _outputCacheProfile.Name.ToLower() == cacheProfileName.ToLower())
                return;

            if (CacheSettingsSection == null)
                throw new Exception("<outputCacheSettings> has not been found in Web.config. Reveiw your Web.config caching section: system.web//caching//outputCacheSettings");

            OutputCacheProfileCollection cacheProfileCollection = CacheSettingsSection.OutputCacheProfiles;

            if (cacheProfileCollection.AllKeys.Contains(cacheProfileName))
            {
                _outputCacheProfile = cacheProfileCollection[cacheProfileName];
                if (_timespan == 0)
                    _timespan = _outputCacheProfile.Duration;
                if (_clientTimeSpan == 0)
                    _clientTimeSpan = _outputCacheProfile.Duration;
            }
            else
            {
                throw new Exception(string.Format("No OutputCacheProfile has been found in Web.config with the name of '{0}'. Reveiw your Web.config caching section: system.web//caching//outputCacheSettings", cacheProfileName));
            }
        }

        //private void SetTimeSpan()
        //{
        //    if (_outputCacheProfile != null)
        //    {
        //        if (_outputCacheProfile.Duration <= 0)
        //            throw new Exception(string.Format("duration field of OutputCacheProfile '{0}' should be a valid positive integer.", _outputCacheProfile.Name));
        //        _timespan = _outputCacheProfile.Duration;
        //    }
        //    else
        //    {
        //        _timespan = int.MaxValue;
        //    }
        //}

        private bool CheckDebugModeDisability()
        {
            bool result = false;
#if DEBUG
            result = this.DisableInDebugMode;
#endif
            return result;
        }

        #endregion

        public override void OnActionExecuting(HttpActionContext filterContext)
        {
            if (CheckDebugModeDisability())
                return;

            if (filterContext == null)
                throw new ArgumentNullException("filterContext");

            if (_isCachingTimeValid(_timespan, filterContext, _anonymousOnly))
            {
                _cachekey = string.Join(":", new string[] { filterContext.Request.RequestUri.PathAndQuery, filterContext.Request.Headers.Accept.FirstOrDefault().ToString() });

                if (WebApiCache.Contains(_cachekey))
                {
                    var val = WebApiCache.Get(_cachekey) as string;

                    if (val != null)
                    {
                        var contenttype = (MediaTypeHeaderValue)WebApiCache.Get(_cachekey + ":response-ct");
                        if (contenttype == null)
                            contenttype = new MediaTypeHeaderValue(_cachekey.Split(':')[1]);

                        filterContext.Response = filterContext.Request.CreateResponse();
                        filterContext.Response.Content = new StringContent(val);

                        filterContext.Response.Content.Headers.ContentType = contenttype;
                        return;
                    }
                }
            }
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (CheckDebugModeDisability())
                return;

            if (!(WebApiCache.Contains(_cachekey)) && !string.IsNullOrWhiteSpace(_cachekey))
            {
                var body = actionExecutedContext.Response.Content.ReadAsStringAsync().Result;
                WebApiCache.Add(_cachekey, body, DateTime.Now.AddSeconds(_timespan));
                WebApiCache.Add(_cachekey + ":response-ct", actionExecutedContext.Response.Content.Headers.ContentType, DateTime.Now.AddSeconds(_timespan));
            }

            if (_isCachingTimeValid(_clientTimeSpan, actionExecutedContext.ActionContext, _anonymousOnly))
                actionExecutedContext.ActionContext.Response.Headers.CacheControl = SetClientCache();
        }
    }
}
