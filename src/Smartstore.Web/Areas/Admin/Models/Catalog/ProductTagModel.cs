﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Smartstore.Web.Modelling;

namespace Smartstore.Admin.Models.Catalog
{
    [LocalizedDisplay("Admin.Catalog.ProductTags.")]
    public class ProductTagModel : EntityModelBase, ILocalizedModel<ProductTagLocalizedModel>
    {
        [Required]
        [LocalizedDisplay("*Fields.Name")]
        public string Name { get; set; }

        [LocalizedDisplay("*Published")]
        public bool Published { get; set; }

        [LocalizedDisplay("*ProductCount")]
        public int ProductCount { get; set; }

        public List<ProductTagLocalizedModel> Locales { get; set; } = new();
    }

    public class ProductTagLocalizedModel : ILocalizedLocaleModel
    {
        public int LanguageId { get; set; }

        [LocalizedDisplay("Admin.Catalog.ProductTags.Fields.Name")]
        public string Name { get; set; }
    }

    // INFO/TODO: (mh) (core) You removed the fluent validator for "Name" because of the [Required] attribute.
    // But we all know that [Required] does NOT output localized messages, only FluentValidator does.
}