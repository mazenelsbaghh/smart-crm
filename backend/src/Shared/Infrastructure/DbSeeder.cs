using Microsoft.EntityFrameworkCore;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Shared.Infrastructure
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(
            AppDbContext context,
            IPasswordHasher passwordHasher,
            IConfiguration configuration)
        {
            Console.WriteLine("🌱 Seeding database...");

            // 1. Seed Default Project
            var defaultProjectId = Guid.Parse("d3b07384-d113-4a15-bbf9-000000000000");
            var projectExists = await context.Projects.AnyAsync(p => p.Id == defaultProjectId);
            if (!projectExists)
            {
                var defaultProject = new Modules.Projects.Domain.Project
                {
                    Id = defaultProjectId,
                    Name = "Default Project",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Projects.Add(defaultProject);
                await context.SaveChangesAsync();
                Console.WriteLine("✅ Default Project seeded.");
            }

            // 2. Seed Project Settings
            var settingsExists = await context.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == defaultProjectId);
            if (!settingsExists)
            {
                var defaultSettings = new Modules.Projects.Domain.ProjectSettings
                {
                    ProjectId = defaultProjectId,
                    AiAutoReplyEnabled = false,
                    Timezone = "Africa/Cairo",
                    GeminiApiKey = string.Empty,
                    UpdatedAt = DateTime.UtcNow
                };
                context.ProjectSettings.Add(defaultSettings);
                await context.SaveChangesAsync();
                Console.WriteLine("✅ Project Settings seeded.");
            }

            // 3. Seed Default User
            var userEmail = configuration["DevelopmentSeed:AdminEmail"];
            var userPassword = configuration["DevelopmentSeed:AdminPassword"];
            if (!string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(userPassword))
            {
                var userExists = await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == userEmail);
                if (!userExists)
                {
                    var adminUser = new Modules.Auth.Domain.User
                    {
                        Id = Guid.NewGuid(),
                        Email = userEmail.Trim(),
                        PasswordHash = passwordHasher.HashPassword(userPassword),
                        Role = "Owner",
                        ProjectId = defaultProjectId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    context.Users.Add(adminUser);
                    await context.SaveChangesAsync();
                    Console.WriteLine("✅ Development admin user seeded from configuration.");
                }
            }
            else Console.WriteLine("ℹ️ Development admin seed skipped; configure DevelopmentSeed credentials if needed.");

            Console.WriteLine("🌱 Seeding complete.");
        }
    }
}
