ASP.NET Web API OutputCache
========================

A simple filter bringing caching options, similar to MVC's "OutputCacheAttribute" to Web API ApiControllers.

Usage:

        [WebApiOutputCache(duration: 120)]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        [WebApiOutputCache(cacheProfile: "cache2min")]
        public string Get(int id)
        {
            return "value";
        }

More feature is comming.... 
