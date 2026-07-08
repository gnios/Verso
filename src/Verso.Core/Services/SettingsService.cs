using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Core.Services;

public class SettingsService(IDbContextFactory<VersoDbContext> dbContextFactory)
{
    private const int SingletonId = 1;

    public async Task<UserSettings> GetAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var settings = await context.UserSettings.FindAsync(SingletonId);

        if (settings is not null)
        {
            return settings;
        }

        settings = new UserSettings { Id = SingletonId };
        context.UserSettings.Add(settings);
        await context.SaveChangesAsync();
        return settings;
    }

    public async Task UpdateAsync(Action<UserSettings> mutate)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var settings = await context.UserSettings.FindAsync(SingletonId);

        if (settings is null)
        {
            settings = new UserSettings { Id = SingletonId };
            context.UserSettings.Add(settings);
        }

        mutate(settings);
        await context.SaveChangesAsync();
    }
}
