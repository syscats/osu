﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;
using OpenTK.Graphics;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.MathUtils;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Cursor;

namespace osu.Game.Screens.Select
{
    public class BeatmapInfoWedge : OverlayContainer
    {
        private static readonly Vector2 wedged_container_shear = new Vector2(0.15f, 0);

        private Drawable info;

        public BeatmapInfoWedge()
        {
            Shear = wedged_container_shear;
            Masking = true;
            BorderColour = new Color4(221, 255, 255, 255);
            BorderThickness = 2.5f;
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(130, 204, 255, 150),
                Radius = 20,
                Roundness = 15,
            };
        }

        protected override bool BlockPassThroughMouse => false;

        protected override void PopIn()
        {
            this.MoveToX(0, 800, Easing.OutQuint);
            this.RotateTo(0, 800, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.MoveToX(-100, 800, Easing.InQuint);
            this.RotateTo(10, 800, Easing.InQuint);
        }

        public void UpdateBeatmap(WorkingBeatmap beatmap)
        {
            LoadComponentAsync(new BufferedWedgeInfo(beatmap)
            {
                Shear = -Shear,
                Depth = info?.Depth + 1 ?? 0,
            }, newInfo =>
            {
                // ensure we ourselves are visible if not already.
                if (!IsPresent)
                    this.FadeIn(250);

                info?.FadeOut(250);
                info?.Expire();

                Add(info = newInfo);
            });
        }

        public class BufferedWedgeInfo : BufferedContainer
        {
            private readonly WorkingBeatmap working;

            public BufferedWedgeInfo(WorkingBeatmap working)
            {
                this.working = working;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var beatmapInfo = working.BeatmapInfo;
                var metadata = beatmapInfo.Metadata ?? working.BeatmapSetInfo?.Metadata ?? new BeatmapMetadata();

                PixelSnapping = true;
                CacheDrawnFrameBuffer = true;
                RelativeSizeAxes = Axes.Both;

                Children = new Drawable[]
                {
                    // We will create the white-to-black gradient by modulating transparency and having
                    // a black backdrop. This results in an sRGB-space gradient and not linear space,
                    // transitioning from white to black more perceptually uniformly.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                    },
                    // We use a container, such that we can set the colour gradient to go across the
                    // vertices of the masked container instead of the vertices of the (larger) sprite.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(Color4.White, Color4.White.Opacity(0.3f)),
                        Children = new[]
                        {
                            // Zoomed-in and cropped beatmap background
                            new BeatmapBackgroundSprite(working)
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                FillMode = FillMode.Fill,
                            },
                        },
                    },
                    new DifficultyColourBar(beatmapInfo)
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 20,
                    },
                    new FillFlowContainer
                    {
                        Name = "Top-aligned metadata",
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Direction = FillDirection.Vertical,
                        Margin = new MarginPadding { Top = 10, Left = 25, Right = 10, Bottom = 20 },
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Font = @"Exo2.0-MediumItalic",
                                Text = beatmapInfo.Version,
                                TextSize = 24,
                            },
                        }
                    },
                    new FillFlowContainer
                    {
                        Name = "Bottom-aligned metadata",
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Direction = FillDirection.Vertical,
                        Margin = new MarginPadding { Top = 15, Left = 25, Right = 10, Bottom = 20 },
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Font = @"Exo2.0-MediumItalic",
                                Text = string.IsNullOrEmpty(metadata.Source) ? metadata.Title : metadata.Source + " — " + metadata.Title,
                                TextSize = 28,
                            },
                            new OsuSpriteText
                            {
                                Font = @"Exo2.0-MediumItalic",
                                Text = metadata.Artist,
                                TextSize = 17,
                            },
                            new FillFlowContainer
                            {
                                Margin = new MarginPadding { Top = 10 },
                                Direction = FillDirection.Horizontal,
                                AutoSizeAxes = Axes.Both,
                                Children = getMapper(metadata)
                            },
                            new FillFlowContainer
                            {
                                Margin = new MarginPadding { Top = 20 },
                                Spacing = new Vector2(20, 0),
                                AutoSizeAxes = Axes.Both,
                                Children = getInfoLabels()
                            }
                        }
                    }
                };
            }

            private InfoLabel[] getInfoLabels()
            {
                var beatmap = working.Beatmap;
                var info = working.BeatmapInfo;

                List<InfoLabel> labels = new List<InfoLabel>();

                if (beatmap?.HitObjects?.Count > 0)
                {
                    HitObject lastObject = beatmap.HitObjects.LastOrDefault();
                    double endTime = (lastObject as IHasEndTime)?.EndTime ?? lastObject?.StartTime ?? 0;

                    labels.Add(new InfoLabel(new BeatmapStatistic
                    {
                        Name = "Length",
                        Icon = FontAwesome.fa_clock_o,
                        Content = beatmap.HitObjects.Count == 0 ? "-" : TimeSpan.FromMilliseconds(endTime - beatmap.HitObjects.First().StartTime).ToString(@"m\:ss"),
                    }));

                    labels.Add(new InfoLabel(new BeatmapStatistic
                    {
                        Name = "BPM",
                        Icon = FontAwesome.fa_circle,
                        Content = getBPMRange(beatmap),
                    }));

                    //get statistics from the current ruleset.
                    labels.AddRange(info.Ruleset.CreateInstance().GetBeatmapStatistics(working).Select(s => new InfoLabel(s)));
                }

                return labels.ToArray();
            }

            private string getBPMRange(Beatmap beatmap)
            {
                double bpmMax = beatmap.ControlPointInfo.BPMMaximum;
                double bpmMin = beatmap.ControlPointInfo.BPMMinimum;

                if (Precision.AlmostEquals(bpmMin, bpmMax))
                    return $"{bpmMin:0}";

                return $"{bpmMin:0}-{bpmMax:0} (mostly {beatmap.ControlPointInfo.BPMMode:0})";
            }

            private OsuSpriteText[] getMapper(BeatmapMetadata metadata)
            {
                if (string.IsNullOrEmpty(metadata.Author?.Username))
                    return Array.Empty<OsuSpriteText>();

                return new[]
                {
                    new OsuSpriteText
                    {
                        Font = @"Exo2.0-Medium",
                        Text = "mapped by ",
                        TextSize = 15,
                    },
                    new OsuSpriteText
                    {
                        Font = @"Exo2.0-Bold",
                        Text = metadata.Author.Username,
                        TextSize = 15,
                    }
                };
            }

            public class InfoLabel : Container, IHasTooltip
            {
                public string TooltipText { get; private set; }

                public InfoLabel(BeatmapStatistic statistic)
                {
                    TooltipText = statistic.Name;
                    AutoSizeAxes = Axes.Both;

                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(20),
                            Children = new[]
                            {
                                new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = OsuColour.FromHex(@"441288"),
                                    Icon = FontAwesome.fa_square,
                                    Rotation = 45,
                                },
                                new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    RelativeSizeAxes = Axes.Both,
                                    Scale = new Vector2(0.8f),
                                    Colour = OsuColour.FromHex(@"f7dd55"),
                                    Icon = statistic.Icon,
                                },
                            }
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = new Color4(255, 221, 85, 255),
                            Font = @"Exo2.0-Bold",
                            Margin = new MarginPadding { Left = 30 },
                            Text = statistic.Content,
                            TextSize = 17,
                        }
                    };
                }
            }

            private class DifficultyColourBar : DifficultyColouredContainer
            {
                public DifficultyColourBar(BeatmapInfo beatmap)
                    : base(beatmap)
                {
                }

                [BackgroundDependencyLoader]
                private void load()
                {
                    const float full_opacity_ratio = 0.7f;

                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = AccentColour,
                            Width = full_opacity_ratio,
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativePositionAxes = Axes.Both,
                            Colour = AccentColour,
                            Alpha = 0.5f,
                            X = full_opacity_ratio,
                            Width = 1 - full_opacity_ratio,
                        }
                    };
                }
            }
        }
    }
}
