using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Threading;
using System.Web.Configuration;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace WebApi.OutputCache
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

        private bool? _disabled;

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
            set
            {
                if (value > 0)
                    _clientTimeSpan = value;
                else
                    throw new Exception("ClientTimeSpan should be greater then zero.");
            }
        }

        public int Duration
        {
            get { return _timespan; }
            set
            {
                if (value > 0)
                {
                    _timespan = value;
                    if (_clientTimeSpan == 0)
                        _clientTimeSpan = _timespan;
                }
                else
                {
                    throw new Exception("Duration should be greater then zero.",
                        new Exception("Duration property of WebApiOutputCacheAttribute should be grater than zero. Reveiw the code or Web.config caching section: system.web//caching//outputCacheSettings."));
                }
            }
        }

        public bool Disabled
        {
            get { return _disabled ?? false; }
            set { _disabled = value; }
        }

        public bool DisabledInDebugMode { get; set; }

        #endregion

        #region Constructors

        public WebApiOutputCacheAttribute(int duration)
        {
            this.Duration = duration;
        }

        public WebApiOutputCacheAttribute(string cacheProfile)
        {
            this.CacheProfile = cacheProfile;
        }

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
                throw new Exception("<outputCacheSettings> has not been found in Web.config. Reveiw Web.config caching section: system.web//caching//outputCacheSettings.");

            OutputCacheProfileCollection cacheProfileCollection = CacheSettingsSection.OutputCacheProfiles;

            if (cacheProfileCollection.AllKeys.Contains(cacheProfileName))
            {
                _outputCacheProfile = cacheProfileCollection[cacheProfileName];
                if (this.Duration == 0)
                    this.Duration = _outputCacheProfile.Duration;
                if (this.ClientTimeSpan == 0)
                    this.ClientTimeSpan = _outputCacheProfile.Duration;
                if (!_disabled.HasValue)
                    this.Disabled = !_outputCacheProfile.Enabled;
            }
            else
            {
                throw new Exception(string.Format("No OutputCacheProfile has been found in Web.config with the name of '{0}'. Reveiw Web.config caching section: system.web//caching//outputCacheSettings.", cacheProfileName));
            }
        }

        private bool OutputCacheIsDisabled()
        {
            bool disabledBecauseOfDebugMode = false;
#if DEBUG
            disabledBecauseOfDebugMode = this.DisabledInDebugMode;
#endif
            return disabledBecauseOfDebugMode || this.Disabled;
        }

        #endregion

        #region ActionFilterAttribute methods implementation

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext == null)
                throw new ArgumentNullException("filterContext");

            if (OutputCacheIsDisabled())
                return;

            if (_isCachingTimeValid(_timespan, actionContext, _anonymousOnly))
            {
                _cachekey = string.Join(":", new string[] {
                                                actionContext.Request.RequestUri.PathAndQuery, 
                                                actionContext.Request.Headers.Accept.FirstOrDefault().ToString(),
                                                Thread.CurrentPrincipal.Identity.IsAuthenticated ? Thread.CurrentPrincipal.Identity.Name : null
                                             });

                if (WebApiCache.Contains(_cachekey))
                {
                    var val = WebApiCache.Get(_cachekey) as string;

                    if (val != null)
                    {
                        var contenttype = (MediaTypeHeaderValue)WebApiCache.Get(_cachekey + ":response-ct");
                        if (contenttype == null)
                            contenttype = new MediaTypeHeaderValue(_cachekey.Split(':')[1]);

                        actionContext.Response = actionContext.Request.CreateResponse();
                        actionContext.Response.Content = new StringContent(val);

                        actionContext.Response.Content.Headers.ContentType = contenttype;
                        return;
                    }
                }
            }
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext == null)
                throw new ArgumentNullException("actionExecutedContext");

            if (OutputCacheIsDisabled())
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

        #endregion
    }
}
