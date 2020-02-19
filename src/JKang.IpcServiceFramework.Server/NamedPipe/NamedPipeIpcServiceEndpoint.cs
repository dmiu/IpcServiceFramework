﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace JKang.IpcServiceFramework.NamedPipe
{
    public class NamedPipeIpcServiceEndpoint<TContract> : IpcServiceEndpoint<TContract>
        where TContract : class
    {
        private readonly ILogger<NamedPipeIpcServiceEndpoint<TContract>> _logger;
        private readonly NamedPipeOptions _options;
        private readonly Func<Stream, Stream> _streamTranslator;

        public NamedPipeIpcServiceEndpoint(string name, IServiceProvider serviceProvider, string pipeName, bool includeFailureDetailsInResponse = false)
            : base(name, serviceProvider, includeFailureDetailsInResponse)
        {
            PipeName = pipeName;

            _logger = serviceProvider.GetService<ILogger<NamedPipeIpcServiceEndpoint<TContract>>>();
            _options = serviceProvider.GetRequiredService<NamedPipeOptions>();
        }

        public NamedPipeIpcServiceEndpoint(string name, IServiceProvider serviceProvider, string pipeName, Func<Stream, Stream> streamTranslator, bool includeFailureDetailsInResponse = false)
            : this(name, serviceProvider, pipeName, includeFailureDetailsInResponse)
        {
            _streamTranslator = streamTranslator;
        }

        public string PipeName { get; }

        public override Task ListenAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            NamedPipeOptions options = ServiceProvider.GetRequiredService<NamedPipeOptions>();

            var threads = new Thread[options.ThreadCount];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((a) => { StartServerThread(a).ConfigureAwait(false).GetAwaiter().GetResult(); });
                threads[i].Start(cancellationToken);
            }

            return Task.Factory.StartNew(() =>
            {
                _logger.LogDebug($"Endpoint '{Name}' listening on pipe '{PipeName}'...");
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);

                    for (int i = 0; i < threads.Length; i++)
                    {
                        if (threads[i].Join(250))
                        {
                            // thread is finished, starting a new thread
                            threads[i] = new Thread((a) => { StartServerThread(a).ConfigureAwait(false).GetAwaiter().GetResult(); });
                            threads[i].Start(cancellationToken);
                        }
                    }
                }
            });
        }

        private async Task StartServerThread(object obj)
        {
            var token = (CancellationToken)obj;
            try
            {
                using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, _options.ThreadCount,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    await ProcessAsync(_streamTranslator?.Invoke(server) ?? server, _logger, token).ConfigureAwait(false);
                }
            }
            catch when (token.IsCancellationRequested)
            {
            }
        }
    }
}
