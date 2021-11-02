﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Smartstore.Admin.Models.Menus;
using Smartstore.ComponentModel;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Core.Stores;
using Smartstore.Data;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling;
using Smartstore.Web.Models.DataGrid;
using Smartstore.Web.Rendering;

namespace Smartstore.Admin.Controllers
{
    [Area("Admin")]
    public class MenuController : AdminController
    {
        private readonly SmartDbContext _db;
        private readonly IStoreMappingService _storeMappingService;
        private readonly ILocalizedEntityService _localizedEntityService;
        private readonly IAclService _aclService;
        private readonly IDictionary<string, Lazy<IMenuItemProvider, MenuItemProviderMetadata>> _menuItemProviders;

        public MenuController(SmartDbContext db,
            IStoreMappingService storeMappingService,
            ILocalizedEntityService localizedEntityService,
            IAclService aclService,
            IEnumerable<Lazy<IMenuItemProvider, MenuItemProviderMetadata>> menuItemProviders)
        {
            _db = db;
            _storeMappingService = storeMappingService;
            _localizedEntityService = localizedEntityService;
            _aclService = aclService;
            _menuItemProviders = menuItemProviders.ToDictionarySafe(x => x.Metadata.ProviderName, x => x);
        }

        #region Utilities

        private async Task PrepareModelAsync(MenuEntityModel model, MenuEntity entity)
        {
            var templateNames = new string[] { "LinkList", "ListGroup", "Dropdown", "Navbar" };

            if (entity != null && ModelState.IsValid)
            {
                model.SelectedStoreIds = await _storeMappingService.GetAuthorizedStoreIdsAsync(entity);
                model.SelectedCustomerRoleIds = await _aclService.GetAuthorizedCustomerRoleIdsAsync(entity);
                model.IsCustomTemplate = entity.Template.HasValue() && !templateNames.Contains(entity.Template);
            }

            model.Locales = new List<MenuEntityLocalizedModel>();

            model.AllTemplates = templateNames
                .Select(x => new SelectListItem { Text = x, Value = x, Selected = x.EqualsNoCase(entity?.Template) })
                .ToList();

            model.AllProviders = _menuItemProviders.Values
                .Select(x => new SelectListItem
                {
                    Text = T("Providers.MenuItems.FriendlyName." + x.Metadata.ProviderName),
                    Value = x.Metadata.ProviderName
                })
                .ToList();

            var entities = await _db.MenuItems.Where(x => x.MenuId == model.Id).ToListAsync();
            model.ItemTree = await entities.GetTreeAsync("EditMenu", _menuItemProviders);
        }

        private async Task PrepareModelAsync(MenuItemModel model, MenuItemEntity entity)
        {
            Lazy<IMenuItemProvider, MenuItemProviderMetadata> provider = null;
            var entities = await _db.MenuItems
                .AsNoTracking()
                .Where(x => x.MenuId == model.MenuId)
                .Include(x => x.Menu)
                .ToDictionaryAsync(x => x.Id, x => x);

            model.Locales = new List<MenuItemLocalizedModel>();
            model.AllItems = new List<SelectListItem>();

            if (entity != null && ModelState.IsValid)
            {
                model.SelectedStoreIds = await _storeMappingService.GetAuthorizedStoreIdsAsync(entity);
                model.SelectedCustomerRoleIds = await _aclService.GetAuthorizedCustomerRoleIdsAsync(entity);
            }

            if (_menuItemProviders.TryGetValue(model.ProviderName, out provider))
            {
                model.ProviderAppendsMultipleItems = provider.Metadata.AppendsMultipleItems;
            }

            // Preset max display order to always insert item at the end.
            if (entity == null && entities.Any())
            {
                var item = entities
                    .Select(x => x.Value)
                    .Where(x => x.ParentItemId == model.ParentItemId)
                    .OrderByDescending(x => x.DisplayOrder)
                    .FirstOrDefault();

                model.DisplayOrder = (item?.DisplayOrder ?? 0) + 1;
            }

            // Create list for selecting parent item.
            var tree = await entities.Values.GetTreeAsync("EditMenu", _menuItemProviders);

            tree.Traverse(x =>
            {
                if (entity != null && entity.Id == x.Value.EntityId)
                {
                    // Ignore. Element cannot be parent itself.
                    model.TitlePlaceholder = x.Value.Text;
                }
                else if (entities.TryGetValue(x.Value.EntityId, out var record) &&
                    _menuItemProviders.TryGetValue(record.ProviderName, out provider) &&
                    provider.Metadata.AppendsMultipleItems)
                {
                    // Ignore. Element cannot have child nodes.
                }
                else if (!x.Value.IsGroupHeader)
                {
                    var path = string.Join(" » ", x.Trail.Skip(1).Select(y => y.Value.Text));
                    model.AllItems.Add(new SelectListItem
                    {
                        Text = path,
                        Value = x.Value.EntityId.ToString(),
                        Selected = entity != null && entity.ParentItemId == x.Value.EntityId
                    });
                }
            });
        }

        private void UpdateLocales(MenuEntity entity, MenuEntityModel model)
        {
            if (model.Locales != null)
            {
                foreach (var localized in model.Locales)
                {
                    _localizedEntityService.ApplyLocalizedValueAsync(entity, x => x.Title, localized.Title, localized.LanguageId);
                }
            }
        }

        private void UpdateLocales(MenuItemEntity entity, MenuItemModel model)
        {
            if (model.Locales != null)
            {
                foreach (var localized in model.Locales)
                {
                    _localizedEntityService.ApplyLocalizedValueAsync(entity, x => x.Title, localized.Title, localized.LanguageId);
                    _localizedEntityService.ApplyLocalizedValueAsync(entity, x => x.ShortDescription, localized.ShortDescription, localized.LanguageId);
                }
            }
        }

        #endregion

        #region Menus

        public IActionResult Index()
        {
            return RedirectToAction(nameof(List));
        }

        [Permission(Permissions.Cms.Menu.Read)]
        public IActionResult List()
        {
            // TODO: (mh) (core) Is this needed
            var model = new MenuEntityListModel();

            ViewBag.AvailableStores = Services.StoreContext.GetAllStores().ToSelectListItems();
            ViewBag.IsSingleStoreMode = Services.StoreContext.IsSingleStoreMode();

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Cms.MessageTemplate.Read)]
        public async Task<IActionResult> List(GridCommand command, MenuEntityListModel model)
        {
            var query = _db.Menus.AsNoTracking();

            if (model.SystemName.HasValue())
            {
                query = query.Where(x => x.SystemName == model.SystemName);
            }
            
            var menuRecords = await query
                .ApplyStoreFilter(model.StoreId)
                .ApplyGridCommand(command)
                .ToPagedList(command)
                .LoadAsync();

            var menuModels = await menuRecords
                .SelectAsync(async x =>
                {
                    var model = await MapperFactory.MapAsync<MenuEntity, MenuEntityModel>(x);
                    model.EditUrl = Url.Action(nameof(Edit), "Menu", new { id = x.Id });
                    return model;
                })
                .AsyncToList();

            var gridModel = new GridModel<MenuEntityModel>
            {
                Rows = menuModels,
                Total = await menuRecords.GetTotalCountAsync()
            };

            return Json(gridModel);
        }

        [Permission(Permissions.Cms.Menu.Create)]
        public async Task<IActionResult> Create()
        {
            var model = new MenuEntityModel();
            await PrepareModelAsync(model, null);
            AddLocales(model.Locales);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Cms.Menu.Create)]
        public async Task<IActionResult> Create(MenuEntityModel model, bool continueEditing, IFormCollection form)
        {
            if (ModelState.IsValid)
            {
                var menu = MiniMapper.Map<MenuEntityModel, MenuEntity>(model);
                menu.WidgetZone = string.Join(",", model.WidgetZone ?? new string[0]).NullEmpty();

                menu.SystemName = menu.SystemName.ToValidPath();
                _db.Menus.Add(menu);
                await _db.SaveChangesAsync();

                // TODO: (mh) (core) Call GetMenuSystemNamesAsync???? It was called in classic by InsertMenu method.

                await SaveStoreMappingsAsync(menu, model.SelectedStoreIds);
                await SaveAclMappingsAsync(menu, model.SelectedCustomerRoleIds);
                UpdateLocales(menu, model);
                await _db.SaveChangesAsync();
                await Services.EventPublisher.PublishAsync(new ModelBoundEvent(model, menu, form));

                NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
                return continueEditing ? RedirectToAction(nameof(Edit), new { id = menu.Id }) : RedirectToAction(nameof(List));
            }

            await PrepareModelAsync(model, null);

            return View(model);
        }

        [Permission(Permissions.Cms.Menu.Read)]
        public async Task<IActionResult> Edit(int id)
        {
            var menu = await _db.Menus.FindByIdAsync(id, false);
            if (menu == null)
            {
                return NotFound();
            }

            var model = MiniMapper.Map<MenuEntity, MenuEntityModel>(menu);
            model.WidgetZone = menu.WidgetZone.SplitSafe(",").ToArray();

            await PrepareModelAsync(model, menu);
            AddLocales(model.Locales, (locale, languageId) =>
            {
                locale.Title = menu.GetLocalized(x => x.Title, languageId, false, false);
            });

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Cms.Menu.Update)]
        public async Task<IActionResult> Edit(MenuEntityModel model, bool continueEditing, IFormCollection form)
        {
            var menu = await _db.Menus.FindByIdAsync(model.Id);
            if (menu == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                MiniMapper.Map(model, menu);
                menu.WidgetZone = string.Join(",", model.WidgetZone ?? new string[0]).NullEmpty();

                // TODO: (mh) (core) Call GetMenuSystemNamesAsync???? It was called in classic by UpdateMenu method.

                await SaveStoreMappingsAsync(menu, model.SelectedStoreIds);
                await SaveAclMappingsAsync(menu, model.SelectedCustomerRoleIds);
                UpdateLocales(menu, model);
                await _db.SaveChangesAsync();
                await Services.EventPublisher.PublishAsync(new ModelBoundEvent(model, menu, form));

                NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
                return continueEditing ? RedirectToAction(nameof(Edit), menu.Id) : RedirectToAction(nameof(List));
            }

            await PrepareModelAsync(model, menu);

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Cms.Menu.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var menu = await _db.Menus.FindByIdAsync(id);
            if (menu == null)
            {
                return NotFound();
            }

            if (menu.IsSystemMenu)
            {
                NotifyError(T("Admin.ContentManagement.Menus.CannotBeDeleted"));
                return RedirectToAction(nameof(Edit), new { id = menu.Id });
            }

            _db.Menus.Remove(menu);

            await _db.SaveChangesAsync();

            NotifySuccess(T("Admin.ContentManagement.MessageTemplates.Deleted"));
            return RedirectToAction(nameof(List));
        }

        [HttpPost]
        [Permission(Permissions.Cms.Menu.Delete)]
        public async Task<IActionResult> Delete(GridSelection selection)
        {
            var success = false;
            var numDeleted = 0;
            var ids = selection.GetEntityIds();

            if (ids.Any())
            {
                var menus = await _db.Menus.GetManyAsync(ids, true);
                var triedToDeleteSystemMenu = false;

                foreach (var menu in menus)
                {
                    if (menu.IsSystemMenu == true)
                    {
                        triedToDeleteSystemMenu = true;
                        NotifyError(T("Admin.Configuration.MessageTemplates.Deleted"));
                    }
                    else
                    {
                        _db.Menus.Remove(menu);
                    }
                }

                numDeleted = await _db.SaveChangesAsync();

                success = !triedToDeleteSystemMenu || numDeleted != 0;
            }

            return Json(new { Success = success, Count = numDeleted });
        }

        #endregion

        #region Menu items

        // Ajax.
        [Permission(Permissions.Cms.Menu.Read)]
        public async Task<IActionResult> ItemList(int id)
        {
            var model = new MenuEntityModel { Id = id };
            await PrepareModelAsync(model, null);

            return View(model);
        }

        [Permission(Permissions.Cms.Menu.Update)]
        public async Task<IActionResult> CreateItem(string providerName, int menuId, int parentItemId)
        {
            var menu = await _db.Menus.FindByIdAsync(menuId);
            if (menu == null)
            {
                return NotFound();
            }

            var model = new MenuItemModel
            {
                ProviderName = providerName,
                MenuId = menuId,
                ParentItemId = parentItemId,
                Published = true
            };

            await PrepareModelAsync(model, null);
            AddLocales(model.Locales);

            return View(model);
        }

        // Do not name parameter "model" because of property of same name.
        [HttpPost, ParameterBasedOnFormName("save-item-continue", "continueEditing")]
        [Permission(Permissions.Cms.Menu.Update)]
        public async Task<IActionResult> CreateItem(MenuItemModel itemModel, bool continueEditing, IFormCollection form)
        {
            if (ModelState.IsValid)
            {
                itemModel.ParentItemId ??= 0;
                var item = MiniMapper.Map<MenuItemModel, MenuItemEntity>(itemModel);
                item.PermissionNames = string.Join(",", itemModel.PermissionNames ?? new string[0]).NullEmpty();

                _db.MenuItems.Add(item);
                await _db.SaveChangesAsync();

                await SaveStoreMappingsAsync(item, itemModel.SelectedStoreIds);
                await SaveAclMappingsAsync(item, itemModel.SelectedCustomerRoleIds);
                UpdateLocales(item, itemModel);

                await _db.SaveChangesAsync();

                await Services.EventPublisher.PublishAsync(new ModelBoundEvent(itemModel, item, form));
                NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));

                if (continueEditing)
                {
                    return RedirectToAction(nameof(EditItem), new { id = item.Id });
                }

                return RedirectToAction(nameof(Edit), new { id = item.MenuId });
            }

            await PrepareModelAsync(itemModel, null);

            return View(itemModel);
        }

        [Permission(Permissions.Cms.Menu.Read)]
        public async Task<IActionResult> EditItem(int id)
        {
            var item = await _db.MenuItems.FindByIdAsync(id);
            if (item == null)
            {
                return NotFound();
            }

            var model = MiniMapper.Map<MenuItemEntity, MenuItemModel>(item);
            model.ParentItemId = item.ParentItemId == 0 ? null : item.ParentItemId;
            model.PermissionNames = item.PermissionNames.SplitSafe(",").ToArray();

            await PrepareModelAsync(model, item);
            AddLocales(model.Locales, (locale, languageId) =>
            {
                locale.Title = item.GetLocalized(x => x.Title, languageId, false, false);
                locale.ShortDescription = item.GetLocalized(x => x.ShortDescription, languageId, false, false);
            });

            return View(model);
        }

        // Do not name parameter "model" because of property of same name.
        [HttpPost, ParameterBasedOnFormName("save-item-continue", "continueEditing")]
        [Permission(Permissions.Cms.Menu.Update)]
        public async Task<IActionResult> EditItem(MenuItemModel itemModel, bool continueEditing, IFormCollection form)
        {
            var item = await _db.MenuItems.FindByIdAsync(itemModel.Id);
            if (item == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                itemModel.ParentItemId ??= 0;
                MiniMapper.Map(itemModel, item);
                item.PermissionNames = string.Join(",", itemModel.PermissionNames ?? new string[0]).NullEmpty();

                await SaveStoreMappingsAsync(item, itemModel.SelectedStoreIds);
                await SaveAclMappingsAsync(item, itemModel.SelectedCustomerRoleIds);
                UpdateLocales(item, itemModel);

                await Services.EventPublisher.PublishAsync(new ModelBoundEvent(itemModel, item, form));
                NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));

                if (continueEditing)
                {
                    return RedirectToAction(nameof(EditItem), new { id = item.Id });
                }

                return RedirectToAction(nameof(Edit), new { id = item.MenuId });
            }

            await PrepareModelAsync(itemModel, item);

            return View(itemModel);
        }

        // Ajax.
        [HttpPost]
        [Permission(Permissions.Cms.Menu.Update)]
        public async Task<IActionResult> MoveItem(int menuId, int sourceId, string direction)
        {
            if (menuId == 0 || sourceId == 0 || direction.IsEmpty())
            {
                return new EmptyResult();
            }

            using (var scope = new DbContextScope(ctx: Services.DbContext))
            {
                var allItems = await _db.MenuItems.Where(x => x.MenuId == menuId).ToDictionaryAsync(x => x.Id, x => x);
                var sourceItem = allItems[sourceId];

                var siblings = allItems.Select(x => x.Value)
                    .Where(x => x.ParentItemId == sourceItem.ParentItemId)
                    .OrderBy(x => x.DisplayOrder)
                    .ToList();

                var index = siblings.IndexOf(sourceItem) + (direction == "up" ? -1 : 1);
                if (index >= 0 && index < siblings.Count)
                {
                    var targetItem = siblings[index];

                    // Ensure unique display order starting from 1.
                    var count = 0;
                    siblings.Each(x => x.DisplayOrder = ++count);

                    // Swap display order of source and target item.
                    var tmp = sourceItem.DisplayOrder;
                    sourceItem.DisplayOrder = targetItem.DisplayOrder;
                    targetItem.DisplayOrder = tmp;

                    scope.Commit();
                }
            }

            return RedirectToAction(nameof(ItemList), new { id = menuId });
        }

        [HttpPost]
        [Permission(Permissions.Cms.Menu.Delete)]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var isAjax = Request.IsAjaxRequest();

            var item = await _db.MenuItems.FindByIdAsync(id);
            if (item == null)
            {
                if (isAjax)
                {
                    return new EmptyResult();
                }

                return NotFound();
            }

            var menuId = item.MenuId;
            _db.MenuItems.Remove(item);
            await _db.SaveChangesAsync();

            NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));

            return RedirectToAction(isAjax ? nameof(ItemList) : nameof(Edit), new { id = menuId });
        }

        #endregion
    }
}