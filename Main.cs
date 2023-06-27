using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudio : IAsyncPlugin
    {
        private PluginInitContext context;

        public Task InitAsync(PluginInitContext context)
        {
            throw new NotImplementedException();
        }

        public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}