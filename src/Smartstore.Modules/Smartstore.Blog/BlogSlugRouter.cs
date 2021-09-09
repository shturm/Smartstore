﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Smartstore.Core.Seo;
using Smartstore.Core.Seo.Routing;

namespace Smartstore.Blog.Seo.Routing
{
    public class BlogSlugRouter : SlugRouter
    {
        // TODO: (mh) (core) Register SlugRouter by calling SlugRouteTransformer.RegisterRouter() in Startup
        public override RouteValueDictionary GetRouteValues(UrlRecord urlRecord, RouteValueDictionary values)
        {
            if (urlRecord.EntityName.ToLowerInvariant() == "blogpost")
            {
                return new RouteValueDictionary
                {
                    { "area", string.Empty },
                    { "controller", "Blog" },
                    // TODO: (mh) (core) Id param is missing
                    { "action", "BlogPost" },
                    { "entity", urlRecord }
                };
            }

            return null;
        }

        public override void MapRoutes(IEndpointRouteBuilder routes)
        {
            routes.MapControllerRoute("BlogPost", UrlPatternFor("BlogPost"), new { controller = "Blog", action = "BlogPost" });
        }
    }
}