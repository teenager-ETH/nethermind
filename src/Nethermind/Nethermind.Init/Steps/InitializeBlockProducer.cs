// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Producers;
using Nethermind.Consensus.Transactions;

namespace Nethermind.Init.Steps
{
    [RunnerStepDependencies(typeof(StartBlockProcessor), typeof(SetupKeyStore), typeof(InitializeNetwork), typeof(ReviewBlockTree))]
    public class InitializeBlockProducer : IStep
    {
        private readonly IApiWithBlockchain _api;

        public InitializeBlockProducer(INethermindApi api)
        {
            _api = api;
        }

        public Task Execute(CancellationToken _)
        {
            if (_api.BlockProductionPolicy!.ShouldStartBlockProduction())
            {
                _api.BlockProducer = BuildProducer();

                _api.BlockProducerRunner = _api.GetConsensusPlugin()!.CreateBlockProducerRunner();

                foreach (IConsensusWrapperPlugin wrapperPlugin in _api.GetConsensusWrapperPlugins().OrderBy(static (p) => p.Priority))
                {
                    _api.BlockProducerRunner = wrapperPlugin.InitBlockProducerRunner(_api.BlockProducerRunner);
                }
            }

            return Task.CompletedTask;
        }

        protected virtual IBlockProducer BuildProducer()
        {
            _api.BlockProducerEnvFactory = new BlockProducerEnvFactory(
                _api.WorldStateManager!,
                _api.BlockTree!,
                _api.SpecProvider!,
                _api.BlockValidator!,
                _api.RewardCalculatorSource!,
                _api.ReceiptStorage!,
                _api.BlockPreprocessor,
                _api.TxPool!,
                _api.TransactionComparerProvider!,
                _api.Config<IBlocksConfig>(),
                _api.LogManager);

            if (_api.ChainSpec is null) throw new StepDependencyException(nameof(_api.ChainSpec));
            IConsensusPlugin? consensusPlugin = _api.GetConsensusPlugin();

            if (consensusPlugin is not null)
            {
                IBlockProducerFactory blockProducerFactory = consensusPlugin;

                foreach (IConsensusWrapperPlugin wrapperPlugin in _api.GetConsensusWrapperPlugins().OrderBy(static (p) => p.Priority))
                {
                    blockProducerFactory = new ConsensusWrapperToBlockProducerFactoryAdapter(wrapperPlugin, blockProducerFactory);
                }

                return blockProducerFactory.InitBlockProducer();
            }
            else
            {
                throw new NotSupportedException($"Mining in {_api.ChainSpec.SealEngineType} mode is not supported");
            }
        }

        private class ConsensusWrapperToBlockProducerFactoryAdapter(
            IConsensusWrapperPlugin consensusWrapperPlugin,
            IBlockProducerFactory baseBlockProducerFactory) : IBlockProducerFactory
        {
            public IBlockProducer InitBlockProducer(ITxSource? additionalTxSource = null)
            {
                return consensusWrapperPlugin.InitBlockProducer(baseBlockProducerFactory, additionalTxSource);
            }
        }
    }
}
