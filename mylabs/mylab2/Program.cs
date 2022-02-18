//#define UseOpenGL // Раскомментировать для использования OpenGL

#if (!UseOpenGL)
using Device = CGLabPlatform.GDIDevice;
using DeviceArgs = CGLabPlatform.GDIDeviceUpdateArgs;
#else
using Device = CGLabPlatform.OGLDevice;
using DeviceArgs = CGLabPlatform.OGLDeviceUpdateArgs;
using SharpGL;
#endif

using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Diagnostics;
using CGLabPlatform;
// ==================================================================================
using CGApplication = MyApp;
using System.ComponentModel;

public abstract class MyApp : CGApplicationTemplate<CGApplication, Device, DeviceArgs>{
    public const int width = 400; // VSPanelWidth ширина поля с кнопками

    public class Vertex{
        private List<Polygon> polygons;
        public Vertex(){ }

        public Vertex(double[] elements){
            Debug.Assert(elements != null && elements.Length == 4);
            Point_InLocalSpace.X = elements[0];
            Point_InLocalSpace.Y = elements[1];
            Point_InLocalSpace.Z = elements[2];
            Point_InLocalSpace.W = elements[3];
        }

        public DVector4 Point_InLocalSpace;
        public DVector4 Point_InWorldSpace;
    }

    public class Polygon{
        public Vertex[] vertecies;
        public DVector4 Normal_InLocalSpace;
        public DVector4 Normal_InWorldSpace;
        public Polygon(){ }

        public Polygon(Vertex[] elements){
            Debug.Assert(elements != null && elements.Length == 3);
            vertecies = elements;
        }

        public bool IsVisible;
        public int Color;
    }


    // TODO: Добавить свойства, поля
    private DMatrix4 _PointTransform;
    private Commands _Commands = Commands.FigureChange;
    private DVector3 _Rotation;
    private DVector3 _Offset;
    private DVector3 _Scale;

    [Flags]
    private enum Commands : int{
        None = 0,
        Transform = 1 << 0,
        FigureChange = 1 << 1
    }

    [DisplayNumericProperty(new[]{
        1d, 0, 0, 0,
        0, 1d, 0, 0,
        0, 0, 1d, 0,
        0, 0, 0, 1d
    }, 0.01, 4, null)]
    public DMatrix4 PointTransform{
        get => _PointTransform;
        set{
            if (value == _PointTransform)
                return;
            _PointTransform = value;
            _Commands |= Commands.Transform;
            OnPropertyChanged();
        }
    }


    [DisplayNumericProperty(100, 0.1, Minimum: 1, Name: "Ширина фигуры")]
    public double BaseRadius{
        get => Get<double>();
        set{
            if (Set<double>(value)) _Commands |= Commands.FigureChange;
        }
    }

    [DisplayNumericProperty(100, 0.1, Minimum: 1, Name: "Высота фигуры")]
    public double PyrHeight{
        get => Get<double>();
        set{
            if (Set<double>(value)) _Commands |= Commands.FigureChange;
        }
    }

    [DisplayNumericProperty(new[]{0d, 0d, 0d}, 0.1, "Смещение")]
    public DVector3 Offset{
        get => _Offset;
        set{
            if (Set<DVector3>(value)){
                _Offset = value;
                UpdateTransformMatrix();
            }
        }
    }

    [DisplayNumericProperty(new[]{0d, 0d, 0d}, 1, "Поворот")]
    public DVector3 Rotation{
        get => _Rotation;
        set{
            if (Set<DVector3>(value)){
                _Rotation = value;
                if (_Rotation.X >= 360) _Rotation.X -= 360;
                if (_Rotation.Y >= 360) _Rotation.Y -= 360;
                if (_Rotation.Z >= 360) _Rotation.Z -= 360;
                if (_Rotation.X < 0) _Rotation.X += 360;
                if (_Rotation.Y < 0) _Rotation.Y += 360;
                if (_Rotation.Z < 0) _Rotation.Z += 360;
                UpdateTransformMatrix();
            }
        }
    }

    [DisplayNumericProperty(new[]{20d, 20d, 20d}, 0.1, "Масштаб")]
    public DVector3 Scale{
        get => _Scale;
        set{
            _Scale = value;
            if (Set<DVector3>(value)) UpdateTransformMatrix();
        }
    }

    public enum Projection{
        [Description("Не задана")] NotSet,
        [Description("Вид спереди")] InFront,
        [Description("Вид сверху")] Above,
        [Description("Вид сбоку")] Sideway,
        [Description("Изометрия")] Isometry
    }

    [DisplayEnumListProperty(Projection.NotSet, "Проекция")]
    public Projection CurProjection{
        get => Get<Projection>();
        set{
            if (Set<Projection>(value)){
                if (CurProjection == Projection.Above){
                    _Scale.Y = 0;
                }
                else if (CurProjection == Projection.InFront){
                    _Scale.Z = 0;
                }
                else if (CurProjection == Projection.Sideway){
                    _Scale.X = 0;
                }
                else if (CurProjection == Projection.Isometry){
                    _Rotation.X = 35;
                    _Rotation.Y = 45;
                    _Rotation.Z = 0;
                }

                UpdateTransformMatrix();
                _Commands |= Commands.Transform;
            }
        }
    }

    public enum Visualization{
        [Description("Не отображать полигоны")]
        NoPolygons,
        [Description("Одним цветом")] OneColor,
        [Description("Случайные цвета")] RandomColor
    }

    [DisplayEnumListProperty(Visualization.NoPolygons, "Способ отрисовки")]
    public abstract Visualization CurVisual{ get; set; }

    [DisplayCheckerProperty(true, "Каркасная визуализация")]
    public abstract bool IsCarcass{ get; set; }

    public DMatrix4 NormalTransform;

    public Vertex[] vertices;
    public Polygon[] polygons;

    public DVector2 ChangePos;
    public DVector2 ChangeAngle;

    public DVector2 ViewSize;
    public DVector2 Automove;
    public DVector2 AutoScale;

    private void UpdateTransformMatrix(){
        _PointTransform = new DMatrix4(new double[]{
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        });

        /* Смещение */
        var offset = new DMatrix4(new double[]{
            1, 0, 0, _Offset.X,
            0, 1, 0, _Offset.Y,
            0, 0, 1, _Offset.Z,
            0, 0, 0, 1
        });
        _PointTransform *= offset;

        /* Поворот */
        // По оси X
        var x_rotate = new DMatrix4(new double[]{
            1, 0, 0, 0,
            0, Math.Cos(Math.PI / 180 * _Rotation.X), Math.Sin(Math.PI / 180 * _Rotation.X), 0,
            0, -Math.Sin(Math.PI / 180 * _Rotation.X), Math.Cos(Math.PI / 180 * _Rotation.X), 0,
            0, 0, 0, 1
        });
        var y_rotate = new DMatrix4(new double[]{
            Math.Cos(Math.PI / 180 * _Rotation.Y), 0, Math.Sin(Math.PI / 180 * _Rotation.Y), 0,
            0, 1, 0, 0,
            -Math.Sin(Math.PI / 180 * _Rotation.Y), 0, Math.Cos(Math.PI / 180 * _Rotation.Y), 0,
            0, 0, 0, 1
        });
        var z_rotate = new DMatrix4(new double[]{
            Math.Cos(Math.PI / 180 * _Rotation.Z), -Math.Sin(Math.PI / 180 * _Rotation.Z), 0, 0,
            Math.Sin(Math.PI / 180 * _Rotation.Z), Math.Cos(Math.PI / 180 * _Rotation.Z), 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        });
        _PointTransform *= x_rotate;
        _PointTransform *= y_rotate;
        _PointTransform *= z_rotate;

        /* Маштабирование */
        _PointTransform *= new DMatrix4(new double[]{
            _Scale.X, 0, 0, 0,
            0, _Scale.Y, 0, 0,
            0, 0, _Scale.Z, 0,
            0, 0, 0, 1
        });

        _Commands |= Commands.Transform;
    }

    public void Create(){
        var VertexSize = 6;
        var PolygonSize = 8;
        vertices = new Vertex[VertexSize]; //6
        polygons = new Polygon[PolygonSize]; //8
        var random = new Random();
        for (var i = 0; i < vertices.Length; ++i) vertices[i] = new Vertex();
        for (var i = 0; i < polygons.Length; ++i){
            polygons[i] = new Polygon();
            polygons[i].Color = random.Next();
        }

        Generate();
    }

    public void Generate(){
        var numPoint = 5;
        for (var i = 0; i < numPoint - 1; ++i){
            vertices[i].Point_InLocalSpace.X = BaseRadius * Math.Cos(2 * i * Math.PI / (numPoint - 1));
            vertices[i].Point_InLocalSpace.Y = BaseRadius * Math.Sin(2 * i * Math.PI / (numPoint - 1));
            vertices[i].Point_InLocalSpace.Z = 0.01;
            vertices[i].Point_InLocalSpace.W = 1;
        }

        vertices[numPoint - 1].Point_InLocalSpace.X = 0;
        vertices[numPoint - 1].Point_InLocalSpace.Y = 0;
        vertices[numPoint - 1].Point_InLocalSpace.Z = -1 * PyrHeight;
        vertices[numPoint - 1].Point_InLocalSpace.W = 1;

        vertices[numPoint].Point_InLocalSpace.X = 0;
        vertices[numPoint].Point_InLocalSpace.Y = 0;
        vertices[numPoint].Point_InLocalSpace.Z = PyrHeight;
        vertices[numPoint].Point_InLocalSpace.W = 1;

        polygons[0].vertecies = new[]{vertices[numPoint - 1], vertices[0], vertices[1]};
        polygons[1].vertecies = new[]{vertices[numPoint - 1], vertices[1], vertices[2]};
        polygons[2].vertecies = new[]{vertices[numPoint - 1], vertices[2], vertices[3]};
        polygons[3].vertecies = new[]{vertices[numPoint - 1], vertices[3], vertices[0]};
        polygons[4].vertecies = new[]{vertices[1], vertices[0], vertices[numPoint]};
        polygons[5].vertecies = new[]{vertices[2], vertices[1], vertices[numPoint]};
        polygons[6].vertecies = new[]{vertices[3], vertices[2], vertices[numPoint]};
        polygons[7].vertecies = new[]{vertices[0], vertices[3], vertices[numPoint]};

        foreach (var p in polygons){
            var first = new DVector4(new double[]{
                p.vertecies[1].Point_InLocalSpace.X - p.vertecies[0].Point_InLocalSpace.X,
                p.vertecies[1].Point_InLocalSpace.Y - p.vertecies[0].Point_InLocalSpace.Y,
                p.vertecies[1].Point_InLocalSpace.Z - p.vertecies[0].Point_InLocalSpace.Z,
                p.vertecies[1].Point_InLocalSpace.W - p.vertecies[0].Point_InLocalSpace.W
            });
            var second = new DVector4(new double[]{
                p.vertecies[2].Point_InLocalSpace.X - p.vertecies[0].Point_InLocalSpace.X,
                p.vertecies[2].Point_InLocalSpace.Y - p.vertecies[0].Point_InLocalSpace.Y,
                p.vertecies[2].Point_InLocalSpace.Z - p.vertecies[0].Point_InLocalSpace.Z,
                p.vertecies[2].Point_InLocalSpace.W - p.vertecies[0].Point_InLocalSpace.W
            });
            p.Normal_InLocalSpace = first * second;
            p.Normal_InLocalSpace.Normalize();
        }
    }

    protected DVector2 FromViewToPhysicalSpace(Vertex point){
        var result = new DVector2();
        result.X = point.Point_InWorldSpace.X / point.Point_InWorldSpace.W;
        result.Y = point.Point_InWorldSpace.Y / point.Point_InWorldSpace.W;
        result.X = result.X * AutoScale.X +
                   Automove.X; // Преобразование координат из видового пространства в физическое
        result.Y = result.Y * AutoScale.Y + Automove.Y;
        return result;
    }

    protected override void OnMainWindowLoad(object sender, EventArgs args){
        // TODO: Инициализация данных
        RenderDevice.BufferBackCol = 0x20;
        ValueStorage.Font = new Font("Arial", 12f);
        ValueStorage.ForeColor = Color.Firebrick;
        ValueStorage.RowHeight = 30;
        ValueStorage.BackColor = Color.BlanchedAlmond;
        MainWindow.BackColor = Color.DarkGoldenrod;
        ValueStorage.RightColWidth = 50;
        VSPanelWidth = width;
        VSPanelLeft = true;
        MainWindow.Size = new Size(2500, 1380);
        MainWindow.StartPosition = FormStartPosition.Manual;
        MainWindow.Location = Point.Empty;

        RenderDevice.GraphicsHighSpeed = false;
        RenderDevice.BufferBackCol = 0x20;

        RenderDevice.MouseMoveWithRightBtnDown += (s, e)
            => Offset += new DVector3(0.35 * Math.Abs(_Scale.X) * e.MovDeltaX, 0.35 * Math.Abs(_Scale.Y) * e.MovDeltaY,
                0);
        RenderDevice.MouseMoveWithLeftBtnDown += (s, e)
            => Rotation += new DVector3(0.1 * e.MovDeltaY, 0.1 * e.MovDeltaX, 0);
        RenderDevice.MouseWheel += (s, e) => Scale += new DVector3(0.05 * e.Delta, 0.05 * e.Delta, 0.05 * e.Delta);
        Create();
    }

    protected override void OnDeviceUpdate(object s, DeviceArgs e){
        if (0 != ((int) _Commands & (int) Commands.FigureChange)){
            _Commands ^= Commands.FigureChange;
            Generate();
        }

        /* Обновление значений, использующихся для перевода в физ. с. к. */
        var x_min = vertices.Min(p => p.Point_InLocalSpace.X / p.Point_InLocalSpace.Z);
        var x_max = vertices.Max(p => p.Point_InLocalSpace.X / p.Point_InLocalSpace.Z);
        var y_min = vertices.Min(p => p.Point_InLocalSpace.Y / p.Point_InLocalSpace.Z);
        var y_max = vertices.Max(p => p.Point_InLocalSpace.Y / p.Point_InLocalSpace.Z);
        ViewSize.X = x_max - x_min;
        ViewSize.Y = y_max - y_min;
        AutoScale.X = .9 * e.Width / ViewSize.X;
        AutoScale.Y = .9 * e.Heigh / ViewSize.Y;
        AutoScale.X = AutoScale.Y = Math.Min(AutoScale.X, AutoScale.Y);
        Automove.X = e.Width / 2 - (x_min + x_max) / 2 * AutoScale.X;
        Automove.Y = e.Heigh / 2 - (y_min + y_max) / 2 * AutoScale.Y;

        if (0 != ((int) _Commands & (int) Commands.Transform)){
            _Commands ^= Commands.Transform;
            // Пересчет преобразования вектора нормали
            NormalTransform = DMatrix3.NormalVecTransf(PointTransform);

            foreach (var v in vertices) v.Point_InWorldSpace = PointTransform * v.Point_InLocalSpace;

            foreach (var p in polygons){
                p.Normal_InWorldSpace = NormalTransform * p.Normal_InLocalSpace;
                p.IsVisible = p.Normal_InWorldSpace.Z < 0;
            }

            polygons.OrderBy(p => Math.Min(p.vertecies[0].Point_InWorldSpace.Z,
                Math.Min(p.vertecies[1].Point_InWorldSpace.Z, p.vertecies[2].Point_InWorldSpace.Z)));
        }

        foreach (var p in polygons){
            if (!p.IsVisible)
                continue;

            if (CurVisual == Visualization.OneColor)
                e.Surface.DrawTriangle(Color.YellowGreen.ToArgb(), FromViewToPhysicalSpace(p.vertecies[0]),
                    FromViewToPhysicalSpace(p.vertecies[1]),
                    FromViewToPhysicalSpace(p.vertecies[2]));
            else if (CurVisual == Visualization.RandomColor)
                e.Surface.DrawTriangle(p.Color, FromViewToPhysicalSpace(p.vertecies[0]),
                    FromViewToPhysicalSpace(p.vertecies[1]),
                    FromViewToPhysicalSpace(p.vertecies[2]));

            if (IsCarcass){
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[0]),
                    FromViewToPhysicalSpace(p.vertecies[1]));
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[1]),
                    FromViewToPhysicalSpace(p.vertecies[2]));
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[2]),
                    FromViewToPhysicalSpace(p.vertecies[0]));
            }
        }
    }
}

// ==================================================================================
public abstract class AppMain : CGApplication{
    [STAThread]
    private static void Main(){
        RunApplication();
    }
}