using Havit.Blazor.Components.Web.Bootstrap;
using Havit.Data.Patterns.Repositories;
using AS24Net.Domain;
using AS24Net.DataLayer.Filters;

namespace AS24Net.DataLayer.Repositories;

public interface ISettingsItemRepository: IRepository<SettingsItem, string>
{
    
}