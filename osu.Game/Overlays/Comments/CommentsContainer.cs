﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Framework.Graphics;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Shapes;
using osu.Game.Online.API.Requests.Responses;
using System.Threading;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Game.Users;
using System.Collections.Generic;
using JetBrains.Annotations;
using osu.Game.Graphics.Sprites;

namespace osu.Game.Overlays.Comments
{
    public class CommentsContainer : CompositeDrawable
    {
        private readonly Bindable<CommentableType> type = new Bindable<CommentableType>();
        private readonly BindableLong id = new BindableLong();

        public readonly Bindable<CommentsSortCriteria> Sort = new Bindable<CommentsSortCriteria>();
        public readonly BindableBool ShowDeleted = new BindableBool();

        protected readonly Bindable<User> User = new Bindable<User>();

        [Resolved]
        private IAPIProvider api { get; set; }

        private GetCommentsRequest request;
        private CancellationTokenSource loadCancellation;
        private int currentPage;

        private FillFlowContainer content;
        private DeletedCommentsCounter deletedCommentsCounter;
        private CommentsShowMoreButton moreButton;
        protected TotalCommentsCounter CommentCounter;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            AddRangeInternal(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background5
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        CommentCounter = new TotalCommentsCounter(),
                        new CommentsHeader
                        {
                            Sort = { BindTarget = Sort },
                            ShowDeleted = { BindTarget = ShowDeleted }
                        },
                        content = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                        },
                        new Container
                        {
                            Name = @"Footer",
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background4
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Children = new Drawable[]
                                    {
                                        deletedCommentsCounter = new DeletedCommentsCounter
                                        {
                                            ShowDeleted = { BindTarget = ShowDeleted }
                                        },
                                        new Container
                                        {
                                            AutoSizeAxes = Axes.Y,
                                            RelativeSizeAxes = Axes.X,
                                            Child = moreButton = new CommentsShowMoreButton
                                            {
                                                Anchor = Anchor.Centre,
                                                Origin = Anchor.Centre,
                                                Margin = new MarginPadding(5),
                                                Action = getComments,
                                                IsLoading = true,
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });

            User.BindTo(api.LocalUser);
        }

        protected override void LoadComplete()
        {
            User.BindValueChanged(_ => refetchComments());
            Sort.BindValueChanged(_ => refetchComments(), true);
            base.LoadComplete();
        }

        /// <param name="type">The type of resource to get comments for.</param>
        /// <param name="id">The id of the resource to get comments for.</param>
        public void ShowComments(CommentableType type, long id)
        {
            this.type.Value = type;
            this.id.Value = id;

            if (!IsLoaded)
                return;

            // only reset when changing ID/type. other refetch ops are generally just changing sort order.
            CommentCounter.Current.Value = 0;

            refetchComments();
        }

        private void refetchComments()
        {
            ClearComments();
            getComments();
        }

        private void getComments()
        {
            if (id.Value <= 0)
                return;

            request?.Cancel();
            loadCancellation?.Cancel();
            request = new GetCommentsRequest(id.Value, type.Value, Sort.Value, currentPage++, 0);
            request.Success += response => Schedule(() => OnSuccess(response));
            api.PerformAsync(request);
        }

        protected void ClearComments()
        {
            currentPage = 1;
            deletedCommentsCounter.Count.Value = 0;
            moreButton.Show();
            moreButton.IsLoading = true;
            content.Clear();
            commentDictionary.Clear();
        }

        private readonly Dictionary<long, DrawableComment> commentDictionary = new Dictionary<long, DrawableComment>();

        protected void OnSuccess(CommentBundle response)
        {
            if (!response.Comments.Any())
            {
                content.Add(new NoCommentsPlaceholder());
                moreButton.Hide();
                return;
            }
            else
            {
                var topLevelComments = appendComments(response);

                LoadComponentsAsync(topLevelComments, loaded =>
                {
                    content.AddRange(loaded);

                    deletedCommentsCounter.Count.Value += response.Comments.Count(c => c.IsDeleted && c.IsTopLevel);
                    CommentCounter.Current.Value = response.Total;

                    if (response.HasMore)
                    {
                        int loadedTopLevelComments = 0;
                        content.Children.OfType<DrawableComment>().ForEach(p => loadedTopLevelComments++);

                        moreButton.Current.Value = response.TopLevelCount - loadedTopLevelComments;
                        moreButton.IsLoading = false;
                    }
                    else
                    {
                        moreButton.Hide();
                    }
                }, (loadCancellation = new CancellationTokenSource()).Token);
            }
        }

        /// <summary>
        /// Appends retrieved comments to the subtree rooted of comments in this page.
        /// </summary>
        /// <param name="bundle">The bundle of comments to add.</param>
        private List<DrawableComment> appendComments([NotNull] CommentBundle bundle)
        {
            var topLevelComments = new List<DrawableComment>();
            var orphaned = new List<Comment>();

            foreach (var comment in bundle.Comments.Concat(bundle.IncludedComments))
            {
                // Exclude possible duplicated comments.
                if (commentDictionary.ContainsKey(comment.Id))
                    continue;

                addNewComment(comment);
            }

            // Comments whose parents were seen later than themselves can now be added.
            foreach (var o in orphaned)
                addNewComment(o);

            return topLevelComments;

            void addNewComment(Comment comment)
            {
                var drawableComment = getDrawableComment(comment);

                if (comment.ParentId == null)
                {
                    // Comments that have no parent are added as top-level comments to the flow.
                    topLevelComments.Add(drawableComment);
                }
                else if (commentDictionary.TryGetValue(comment.ParentId.Value, out var parentDrawable))
                {
                    // The comment's parent has already been seen, so the parent<-> child links can be added.
                    comment.ParentComment = parentDrawable.Comment;
                    parentDrawable.Replies.Add(drawableComment);
                }
                else
                {
                    // The comment's parent has not been seen yet, so keep it orphaned for the time being. This can occur if the comments arrive out of order.
                    // Since this comment has now been seen, any further children can be added to it without being orphaned themselves.
                    orphaned.Add(comment);
                }
            }
        }

        private DrawableComment getDrawableComment(Comment comment)
        {
            if (commentDictionary.TryGetValue(comment.Id, out var existing))
                return existing;

            return commentDictionary[comment.Id] = new DrawableComment(comment)
            {
                ShowDeleted = { BindTarget = ShowDeleted },
                Sort = { BindTarget = Sort },
                RepliesRequested = onCommentRepliesRequested
            };
        }

        private void onCommentRepliesRequested(DrawableComment drawableComment, int page)
        {
            var request = new GetCommentsRequest(id.Value, type.Value, Sort.Value, page, drawableComment.Comment.Id);

            request.Success += response => Schedule(() => appendComments(response));

            api.PerformAsync(request);
        }

        protected override void Dispose(bool isDisposing)
        {
            request?.Cancel();
            loadCancellation?.Cancel();
            base.Dispose(isDisposing);
        }

        private class NoCommentsPlaceholder : CompositeDrawable
        {
            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                Height = 80;
                RelativeSizeAxes = Axes.X;
                AddRangeInternal(new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background4
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 50 },
                        Text = @"No comments yet."
                    }
                });
            }
        }
    }
}
