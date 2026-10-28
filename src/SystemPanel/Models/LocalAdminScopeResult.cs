using System.Collections.Generic;

namespace SystemPanel.Models
{
    public sealed class LocalAdminScopeResult
    {
        public List<AdUser> Users { get; set; } = new();
        public List<AdComputer> Computers { get; set; } = new();
    }
}
