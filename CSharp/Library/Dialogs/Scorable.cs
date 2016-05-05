﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Bot Builder SDK Github:
// https://github.com/Microsoft/BotBuilder
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Connector;

namespace Microsoft.Bot.Builder.Dialogs.Internals
{
    public interface IScorable<Score>
    {
        Task<object> PrepareAsync<Item>(Item item, Delegate method);
        bool TryScore(object state, out Score score);
        Task PostAsync<Item>(IDialogTask task, Item item, object state);
    }

    public sealed class ScoringDialogTask<Score> : DelegatingDialogTask
    {
        private readonly IComparer<Score> comparer;
        private readonly ITraits<Score> traits;
        private readonly IScorable<Score>[] scorables;
        public ScoringDialogTask(IDialogTask inner, IComparer<Score> comparer, ITraits<Score> traits, params IScorable<Score>[] scorables)
            : base(inner)
        {
            SetField.NotNull(out this.comparer, nameof(comparer), comparer);
            SetField.NotNull(out this.traits, nameof(traits), traits);
            SetField.NotNull(out this.scorables, nameof(scorables), scorables);
        }

        public override async Task PostAsync<T>(T item, CancellationToken cancellationToken = default(CancellationToken))
        {
            Score maximumScore = default(Score);
            object maximumState = null;
            IScorable<Score> maximumScorable = null;

            Func<IScorable<Score>, Delegate, Task<bool>> UpdateAsync = async (scorable, frame) =>
            {
                var state = await scorable.PrepareAsync(item, frame);
                Score score;
                if (scorable.TryScore(state, out score))
                {
                    if (this.comparer.Compare(score, this.traits.Minimum) < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(score));
                    }

                    if (this.comparer.Compare(score, this.traits.Maximum) > 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(score));
                    }

                    var compare = this.comparer.Compare(score, maximumScore);
                    if (maximumScorable == null || compare > 0)
                    {
                        maximumScore = score;
                        maximumState = state;
                        maximumScorable = scorable;

                        if (this.comparer.Compare(score, this.traits.Maximum) == 0)
                        {
                            return false;
                        }
                    }
                }

                return true;
            };

            bool more = true;

            foreach (var frame in this.Frames)
            {
                var scorable = frame.Target as IScorable<Score>;
                if (scorable != null)
                {
                    more = await UpdateAsync(scorable, frame);
                    if (!more)
                    {
                        break;
                    }
                }
            }

            if (more)
            {
                foreach (var scorable in this.scorables)
                {
                    more = await UpdateAsync(scorable, null);
                    if (!more)
                    {
                        break;
                    }
                }
            }

            if (maximumScorable != null)
            {
                await maximumScorable.PostAsync<T>(this.inner, item, maximumState);
                await base.PollAsync();
            }
            else
            {
                await base.PostAsync<T>(item, cancellationToken);
            }
        }
    }
}
