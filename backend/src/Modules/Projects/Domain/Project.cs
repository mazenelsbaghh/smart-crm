using Shared.Domain;
using System;

namespace Modules.Projects.Domain
{
    public class Project : AuditableEntity
    {
        public string Name { get; set; }
    }
}
