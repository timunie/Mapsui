﻿// Copyright 2008 - Paul den Dulk (Geodan)
// 
// This file is part of Mapsui.
// Mapsui is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// Mapsui is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.

// You should have received a copy of the GNU Lesser General Public License
// along with Mapsui; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA f

using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Rendering;
using Mapsui.Rendering.XamlRendering;
using Mapsui.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Mapsui.Windows
{
    public class MapControl : Grid
    {
        #region Fields

        private Map map;
        private readonly Viewport viewport = new Viewport();
        private Point previousMousePosition;
        private Point currentMousePosition;
        private string errorMessage;
        private readonly DoubleAnimation zoomAnimation = new DoubleAnimation();
        private readonly Storyboard zoomStoryBoard = new Storyboard();
        private bool mouseDown;
        private bool viewInitialized;
        private readonly Canvas renderCanvas = new Canvas();
        private readonly IRenderer renderer;
        private bool invalid;
        private readonly Rectangle bboxRect;
        private readonly Canvas canvas;
        Point previousPosition;

        #endregion

        #region EventHandlers

        public event EventHandler ErrorMessageChanged;
        public event EventHandler<ViewChangedEventArgs> ViewChanged;
        public event EventHandler<MouseInfoEventArgs> MouseInfoOver;
        public event EventHandler MouseInfoLeave;
        public event EventHandler<MouseInfoEventArgs> MouseInfoDown;
        public event EventHandler<FeatureInfoEventArgs> FeatureInfo;

        #endregion

        #region Properties

        public IList<ILayer> MouseInfoOverLayers { get; private set; }
        public IList<ILayer> MouseInfoDownLayers { get; private set; }

        public bool ZoomToBoxMode { get; set; }
        public Viewport Viewport { get { return viewport; } }


        public Map Map
        {
            get
            {
                return map;
            }
            set
            {
                if (map != null)
                {
                    var temp = map;
                    map = null;
                    temp.PropertyChanged -= MapPropertyChanged;
                    temp.Dispose();
                }

                map = value;
                //all changes of all layers are returned through this event handler on the map
                if (map != null)
                {
                    map.DataChanged += MapDataChanged;
                    map.PropertyChanged += MapPropertyChanged;
                    map.ViewChanged(true, viewport.Extent, viewport.Resolution);
                }
                OnViewChanged(true);
                RefreshGraphics();
            }
        }

        void MapPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Envelope")
            {
                InitializeView();
                map.ViewChanged(true, viewport.Extent, viewport.Resolution);
            }
        }

        public string ErrorMessage
        {
            get
            {
                return errorMessage;
            }
        }

        public bool ZoomLocked { get; set; }

        #endregion

        #region Dependency Properties

        private static readonly DependencyProperty ResolutionProperty =
          DependencyProperty.Register(
          "Resolution", typeof(double), typeof(MapControl),
          new PropertyMetadata(null, OnResolutionChanged));

        #endregion

        public MapControl()
        {
            canvas = new Canvas
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Colors.Transparent),
                };
            Children.Add(canvas);

            bboxRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Colors.Red),
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeThickness = 3,
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                    StrokeDashArray = new DoubleCollection { 3.0 },
                    Opacity = 0.3,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Visibility = Visibility.Collapsed
                };
            Children.Add(bboxRect);
            
            Map = new Map();
            MouseInfoOverLayers = new List<ILayer>();
            MouseInfoDownLayers = new List<ILayer>();
            Loaded += MapControlLoaded;

            SizeChanged += MapControlSizeChanged;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            canvas.Children.Add(renderCanvas);
            renderer = new MapRenderer(renderCanvas);
            PointerWheelChanged += MapControl_PointerWheelChanged;
            
            ManipulationMode = ManipulationModes.Scale | ManipulationModes.TranslateX | ManipulationModes.TranslateY;     
            ManipulationDelta += OnManipulationDelta;
            ManipulationCompleted += OnManipulationCompleted;
            ManipulationInertiaStarting += OnManipulationInertiaStarting;
        }

        void MapControl_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (ZoomLocked)
                return;

            currentMousePosition = e.GetCurrentPoint(this).RawPosition; //Needed for both MouseMove and MouseWheel event for mousewheel event

            var toResolution = viewport.Resolution;

            if (double.IsNaN(toResolution))
            {
                toResolution = viewport.Resolution;
            }

            if (e.GetCurrentPoint(this).Properties.MouseWheelDelta > 0)
            {
                toResolution = ZoomHelper.ZoomIn(map.Resolutions, toResolution);
            }
            else if (e.GetCurrentPoint(this).Properties.MouseWheelDelta < 0)
            {
                toResolution = ZoomHelper.ZoomOut(map.Resolutions, toResolution);
            }

            e.Handled = true; //so that the scroll event is not sent to the html page.

            //some cheating for personal gain
            viewport.Resolution = toResolution;
            viewport.CenterX += 0.000000001;
            viewport.CenterY += 0.000000001;
            map.ViewChanged(false, viewport.Extent, viewport.Resolution);
            OnViewChanged(false, true);
        }

        #region Public methods

        public void OnViewChanged(bool changeEnd)
        {
            OnViewChanged(changeEnd, false);
        }

        private void OnViewChanged(bool changeEnd, bool userAction)
        {
            if (map != null)
            {
                if (ViewChanged != null)
                {
                    ViewChanged(this, new ViewChangedEventArgs { Viewport = viewport, UserAction = userAction });
                }
            }
        }

        public void Refresh()
        {
            map.ViewChanged(true, viewport.Extent, viewport.Resolution);
            RefreshGraphics();
        }

        private void RefreshGraphics() //should be private soon
        {
            InvalidateArrange();
            InvalidateMeasure();
            canvas.InvalidateArrange();
            canvas.InvalidateMeasure();
            invalid = true;
        }

        public void Clear()
        {
            if (map != null)
            {
                map.ClearCache();
            }
            RefreshGraphics();
        }

        public void ZoomIn()
        {
            if (ZoomLocked)
                return;

            var toResolution = viewport.Resolution;

            if (double.IsNaN(toResolution))
                toResolution = viewport.Resolution;

            toResolution = ZoomHelper.ZoomIn(map.Resolutions, toResolution);
            viewport.Resolution = toResolution;
        }

        public void ZoomOut()
        {
            if (ZoomLocked)
                return;

            var toResolution = viewport.Resolution;

            if (double.IsNaN(toResolution))
                toResolution = viewport.Resolution;

            toResolution = ZoomHelper.ZoomOut(map.Resolutions, toResolution);
            viewport.Resolution = toResolution;
        }

        #endregion

        #region Protected and private methods

        protected void OnErrorMessageChanged(EventArgs e)
        {
            if (ErrorMessageChanged != null)
            {
                ErrorMessageChanged(this, e);
            }
        }

        private static void OnResolutionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var newResolution = (double)e.NewValue;
            ((MapControl)dependencyObject).ZoomIn(newResolution);
        }

        private void ZoomIn(double resolution)
        {
            Point mousePosition = currentMousePosition;
            // When zooming we want the mouse position to stay above the same world coordinate.
            // We calcultate that in 3 steps.

            // 1) Temporarily center on the mouse position
            viewport.Center = viewport.ScreenToWorld(mousePosition.X, mousePosition.Y);

            // 2) Then zoom 
            viewport.Resolution = resolution;

            // 3) Then move the temporary center of the map back to the mouse position
            viewport.Center = viewport.ScreenToWorld(
              viewport.Width - mousePosition.X,
              viewport.Height - mousePosition.Y);

            map.ViewChanged(true, viewport.Extent, viewport.Resolution);
            OnViewChanged(true);
            RefreshGraphics();
        }

        private void MapControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!viewInitialized) InitializeView();
            UpdateSize();
            InitAnimation();
        }

        private void InitAnimation()
        {
            zoomAnimation.Duration = new Duration(new TimeSpan(0, 0, 0, 0, 1000));
            zoomAnimation.EasingFunction = new QuarticEase();
            Storyboard.SetTarget(zoomAnimation, this);
            Storyboard.SetTargetProperty(zoomAnimation, "Resolution");
            zoomStoryBoard.Children.Add(zoomAnimation);
        }

        private void MapControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!viewInitialized) InitializeView();
            Clip = new RectangleGeometry {Rect = new Rect(0, 0, ActualWidth, ActualWidth)};
            UpdateSize();
            map.ViewChanged(true, viewport.Extent, viewport.Resolution);
            OnViewChanged(true);
            Refresh();
        }

        private void UpdateSize()
        {
            var rect = new RectangleGeometry();
            rect.Rect = new Rect(0f, 0f, ActualWidth, ActualHeight);

            if (Viewport != null)
            {
                viewport.Width = ActualWidth;
                viewport.Height = ActualHeight;
            }
        }

        public void MapDataChanged(object sender, DataChangedEventArgs e)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => MapDataChanged(sender, e));
            }
            else
            {
                if (e.Cancelled)
                {
                    errorMessage = "Cancelled";
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else if (e.Error is System.Net.WebException)
                {
                    errorMessage = "WebException: " + e.Error.Message;
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else if (e.Error != null)
                {
                    errorMessage = e.Error.GetType() + ": " + e.Error.Message;
                    OnErrorMessageChanged(EventArgs.Empty);
                }
                else // no problems
                {
                    RefreshGraphics();
                }
            }
        }

        private void RaiseMouseInfoEvents(Point mousePosition)
        {
            if (!mouseDown)
            {
                var mouseEventArgs = GetMouseInfoEventArgs(mousePosition, MouseInfoOverLayers);
                if (mouseEventArgs == null) OnMouseInfoLeave();
                else OnMouseInfoOver(mouseEventArgs);
            }
        }

        private MouseInfoEventArgs GetMouseInfoEventArgs(Point mousePosition, IEnumerable<ILayer> layers)
        {
            var margin = 8 * Viewport.Resolution;
            var point = Viewport.ScreenToWorld(new Geometries.Point(mousePosition.X, mousePosition.Y));

            foreach (var layer in layers)
            {
                var feature = layer.GetFeaturesInView(Map.Envelope, 0).FirstOrDefault(f =>
                    f.Geometry.GetBoundingBox().GetCentroid().Distance(point) < margin);
                if (feature != null)
                {
                    return new MouseInfoEventArgs { LayerName = layer.LayerName, Feature = feature };
                }
            }
            return null;
        }

        protected void OnMouseInfoLeave()
        {
            if (MouseInfoLeave != null)
            {
                MouseInfoLeave(this, new EventArgs());
            }
        }

        protected void OnMouseInfoOver(MouseInfoEventArgs e)
        {
            if (MouseInfoOver != null)
            {
                MouseInfoOver(this, e);
            }
        }

        protected void OnMouseInfoDown(MouseInfoEventArgs e)
        {
            if (MouseInfoDown != null)
            {
                MouseInfoDown(this, e);
            }
        }

        private void InitializeView()
        {
            if (ActualWidth.IsNanOrZero()) return;
            if (map == null) return;
            if (map.Envelope == null) return;
            if (map.Envelope.Width.IsNanOrZero()) return;
            if (map.Envelope.Height.IsNanOrZero()) return;
            if (map.Envelope.GetCentroid() == null) return;

            if ((viewport.CenterX > 0) && (viewport.CenterY > 0) && (viewport.Resolution > 0))
            {
                viewInitialized = true; //view was already initialized
                return;
            }

            viewport.Center = map.Envelope.GetCentroid();
            viewport.Resolution = map.Envelope.Width / ActualWidth;
            viewInitialized = true;
        }

        void CompositionTarget_Rendering(object sender, object e)
        {
            if (!viewInitialized) InitializeView();
            if (!viewInitialized) return; //stop if the line above failed. 
            if (!invalid) return;

            if ((renderer != null) && (map != null))
            {
                renderer.Render(viewport, map.Layers);
                invalid = false;
            }
        }

        #endregion

        #region Bbox zoom

        public void ZoomToBox(Geometries.Point beginPoint, Geometries.Point endPoint)
        {
            double x, y, resolution;
            var width = Math.Abs(endPoint.X - beginPoint.X);
            var height = Math.Abs(endPoint.Y - beginPoint.Y);
            if (width <= 0) return;
            if (height <= 0) return;

            ZoomHelper.ZoomToBoudingbox(beginPoint.X, beginPoint.Y, endPoint.X, endPoint.Y, ActualWidth, out x, out y, out resolution);
            resolution = ZoomHelper.ClipToExtremes(map.Resolutions, resolution);

            viewport.Center = new Geometries.Point(x, y);
            viewport.Resolution = resolution;

            map.ViewChanged(true, viewport.Extent, viewport.Resolution);
            OnViewChanged(true, true);
            RefreshGraphics();
            ClearBBoxDrawing();
        }

        private void ClearBBoxDrawing()
        {
            bboxRect.Margin = new Thickness(0, 0, 0, 0);
            bboxRect.Width = 0;
            bboxRect.Height = 0;
        }

        #endregion

        public void ZoomToFullEnvelope()
        {
            if (Map.Envelope == null) return;
            if (ActualWidth.IsNanOrZero()) return;
            viewport.Resolution =  Map.Envelope.Width / ActualWidth;
            viewport.Center = Map.Envelope.GetCentroid();
        }

        private static void OnManipulationInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
        {
            e.TranslationBehavior.DesiredDeceleration = 25 * 96.0 / (1000.0 * 1000.0);
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (previousPosition == default(Point) || double.IsNaN(previousPosition.X))
            {
                previousPosition = e.Position;
                return;
            }

            if (Distance(e.Position.X, e.Position.Y, previousPosition.X, previousPosition.Y) > 50)
            {
                previousPosition = default(Point);
                return;
            }

            viewport.Transform(e.Position.X, e.Position.Y, previousPosition.X, previousPosition.Y, e.Delta.Scale);

            previousPosition = e.Position;
            
            invalid = true;
        }

        public static double Distance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x1 - x2, 2.0) + Math.Pow(y1 - y2, 2.0));
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            previousPosition = default(Point);
            Refresh();
        }
    }

    public class ViewChangedEventArgs : EventArgs
    {
        public Viewport Viewport { get; set; }
        public bool UserAction { get; set; }
    }

    public class MouseInfoEventArgs : EventArgs
    {
        public MouseInfoEventArgs()
        {
            LayerName = string.Empty;
        }

        public string LayerName { get; set; }
        public IFeature Feature { get; set; }
    }

    public class FeatureInfoEventArgs : EventArgs
    {
        public IDictionary<string, IEnumerable<IFeature>> FeatureInfo { get; set; }
    }
}