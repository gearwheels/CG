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
    #region Классы

    public class Vertex{
        public Vertex(){ }

        public Vertex(double[] elements){
            Debug.Assert(elements != null && elements.Length == 4);
            Point_InLocalSpace.X = elements[0];
            Point_InLocalSpace.Y = elements[1];
            Point_InLocalSpace.Z = elements[2];
            Point_InLocalSpace.W = elements[3];
            polygons = new List<Polygon>();
        }

        public DVector4 Point_InLocalSpace;
        public DVector4 Point_InWorldSpace;
        public List<Polygon> polygons;
        public Color LightColor;
    }

    public class Polygon{
        public Vertex[] vertecies;
        public DVector4 Normal_InLocalSpace;
        public DVector4 Normal_InWorldSpace;
        public Polygon(){ }

        public Polygon(Vertex[] elements, int randomColor){
            Debug.Assert(elements != null && elements.Length == 3);
            vertecies = elements;
            foreach (var v in elements) v.polygons.Add(this);
            RandomColor = randomColor;
        }

        public bool IsVisible;
        public int RandomColor;
        public Color LightColor;
    }

    #endregion


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
        FigureChange = 1 << 1,
        NewFigure = 1 << 2,
        ShadingChange = 1 << 3
    }

    #region Свойства

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

    [DisplayNumericProperty(1, 0.1, Minimum: 1, Name: "Радиус")]
    public double Radius{
        get => Get<double>();
        set{
            if (Set<double>(value)) _Commands |= Commands.FigureChange;
        }
    }

    [DisplayNumericProperty(new[]{12d, 16d}, Minimum: 4d, Increment: 2d, Name: "Апроксимация")]
    public DVector2 Approximation{
        get => Get<DVector2>();
        set{
            if (Set<DVector2>(value)) _Commands |= Commands.NewFigure;
        }
    }

    public int approx0, approx1;


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

    [DisplayNumericProperty(new[]{1d, 1d, 1d}, Minimum: 0.1d, Increment: 0.1, Name: "Масштаб")]
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
        [Description("Случайные цвета")] RandomColor,
        [Description("Плоское затенение")] FlatShading,
        [Description("Метод затенения Гуро")] GuroShading
    }

    [DisplayEnumListProperty(Visualization.GuroShading, "Способ отрисовки")]
    public Visualization CurVisual{
        get => Get<Visualization>();
        set{
            if (Set<Visualization>(value))
                if (value == Visualization.FlatShading || value == Visualization.GuroShading)
                    _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayCheckerProperty(false, "Каркасная визуализация")]
    public abstract bool IsCarcass{ get; set; }

    [DisplayCheckerProperty(true, "Отображать источник света")]
    public abstract bool IsLightSource{ get; set; }


    [DisplayNumericProperty(new[]{0.68d, 0.85d, 0.90d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d,
        Name: "Цвет материала")]
    public DVector3 MaterialColor{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(new[]{0.14d, 0.14d, 0.20d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d,
        Name: "Ka материала")]
    public DVector3 Ka_Material{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }


    [DisplayNumericProperty(new[]{1d, 1d, 0.54d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d, Name: "Kd материала")]
    public DVector3 Kd_Material{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(new[]{0.21d, 0.21d, 1d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d, Name: "Ks материала")]
    public DVector3 Ks_Material{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(1d, Minimum: 0.01d, Maximum: 100d, Increment: 0.01d, Name: "p материала")]
    public double P_Material{
        get => Get<double>();
        set{
            if (Set<double>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(new[]{1d, 1d, 1d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d, Name: "Ia освещения")]
    public DVector3 Ia_Material{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(new[]{1d, 0.5d, 0d}, Minimum: 0d, Maximum: 1d, Increment: 0.01d, Name: "Il освещения")]
    public DVector3 Il_Material{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(new[]{2.5d, 0.5d, 2d}, 0.01d, "Pos освещения")]
    public DVector3 LightPos{
        get => Get<DVector3>();
        set{
            if (Set<DVector3>(value)) _Commands |= Commands.Transform;
        }
    }

    public DVector4 LightPos_InWorldSpace;

    [DisplayNumericProperty(new[]{0.1d, 0.35d}, Minimum: 0.01d, Maximum: 100d, Increment: 0.01d, Name: "md, mk")]
    public DVector2 Parameters{
        get => Get<DVector2>();
        set{
            if (Set<DVector2>(value)) _Commands |= Commands.ShadingChange;
        }
    }


    public DMatrix4 NormalTransform;

    public Vertex[] vertices;
    public Polygon[] polygons;

    public DVector2 ChangePos;
    public DVector2 ChangeAngle;

    public DVector2 ViewSize;
    public DVector2 Automove;
    public DVector2 AutoScale;
    public DVector2 Center;

    #endregion


    #region Методы

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
            0, Math.Cos(Math.PI / 180 * _Rotation.X), -Math.Sin(Math.PI / 180 * _Rotation.X), 0,
            0, Math.Sin(Math.PI / 180 * _Rotation.X), Math.Cos(Math.PI / 180 * _Rotation.X), 0,
            0, 0, 0, 1
        });
        var y_rotate = new DMatrix4(new double[]{
            Math.Cos(Math.PI / 180 * _Rotation.Y), 0, -Math.Sin(Math.PI / 180 * _Rotation.Y), 0,
            0, 1, 0, 0,
            Math.Sin(Math.PI / 180 * _Rotation.Y), 0, Math.Cos(Math.PI / 180 * _Rotation.Y), 0,
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
        approx0 = (int) Approximation[0]; // меридианы   
        approx1 = (int) Approximation[1]; //параллели                         

        Generate();
    }

    public void Generate(){
        var LLvertices = new List<List<Vertex>>();
        var Lpolygons = new List<Polygon>();
        var phi = 2 * Math.PI / approx0; // горизонтально 
        var theta = 2 * Math.PI / approx1; // вертикально 

        double sumphi = 0;
        double sumtheta = 0;
        var indx = 0;
        var random = new Random();

        for (var i = 0; i < approx1 / 2 - 1; i++){
            sumtheta += theta;
            LLvertices.Add(new List<Vertex>());
            for (var j = 0; j < approx0; j++){
                sumphi += phi;
                LLvertices[LLvertices.Count - 1].Add(new Vertex(new[]{
                    Radius * Math.Sin(sumtheta) * Math.Cos(sumphi), Radius * Math.Sin(sumtheta) * Math.Sin(sumphi),
                    Radius * Math.Cos(sumtheta), 1
                }));
            }

            sumphi = 0;
        }

        LLvertices.Add(new List<Vertex>());
        LLvertices[LLvertices.Count - 1].Add(new Vertex(new[]{0, 0, -Radius, 1})); // i - 1      ________
        LLvertices.Add(new List<Vertex>());
        LLvertices[LLvertices.Count - 1].Add(new Vertex(new[]{0, 0, Radius, 1})); // i    +++++

        for (var i = 0; i < LLvertices.Count - 3; i++){
            for (var j = 0; j < LLvertices[i].Count - 1; j++){
                Lpolygons.Add(new Polygon(
                    new Vertex[]{LLvertices[i + 1][j + 1], LLvertices[i][j + 1], LLvertices[i][j]}, random.Next()));
                Lpolygons.Add(new Polygon(
                    new Vertex[]{LLvertices[i + 1][j], LLvertices[i + 1][j + 1], LLvertices[i][j]}, random.Next()));
            }

            Lpolygons.Add(new Polygon(
                new Vertex[]{LLvertices[i + 1][0], LLvertices[i][0], LLvertices[i][LLvertices[i].Count - 1]},
                random.Next()));
            Lpolygons.Add(new Polygon(
                new Vertex[]{
                    LLvertices[i + 1][LLvertices[i + 1].Count - 1], LLvertices[i + 1][0],
                    LLvertices[i][LLvertices[i].Count - 1]
                }, random.Next()));
        }

        // верхняя крышка 
        for (var i = LLvertices[0].Count - 1; i > 0; i--)
            Lpolygons.Add(new Polygon(
                new Vertex[]{LLvertices[0][i], LLvertices[LLvertices.Count - 1][0], LLvertices[0][i - 1]},
                random.Next()));
        Lpolygons.Add(new Polygon(
            new Vertex[]{LLvertices[0][0], LLvertices[LLvertices.Count - 1][0], LLvertices[0][LLvertices[0].Count - 1]},
            random.Next()));
        // нижняя крышка
        for (var i = LLvertices[LLvertices.Count - 3].Count - 1; i > 0; i--)
            Lpolygons.Add(new Polygon(
                new Vertex[]{
                    LLvertices[LLvertices.Count - 3][i - 1], LLvertices[LLvertices.Count - 2][0],
                    LLvertices[LLvertices.Count - 3][i]
                }, random.Next())); //random.Next()
        Lpolygons.Add(new Polygon(
            new Vertex[]{
                LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1],
                LLvertices[LLvertices.Count - 2][0], LLvertices[LLvertices.Count - 3][0]
            }, random.Next())); //LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1 - 1]
        vertices = new Vertex[(LLvertices.Count - 2) * LLvertices[0].Count + 2];
        polygons = new Polygon[Lpolygons.Count];
        for (var i = 0; i < vertices.Length; ++i) vertices[i] = new Vertex();
        var art_it = 0;
        foreach (var iter in LLvertices)
        foreach (var iter1 in iter){
            vertices[art_it] = iter1;
            ++art_it;
        }

        for (var i = 0; i < polygons.Length; ++i){
            polygons[i] = new Polygon();
            polygons[i] = Lpolygons[i];
        }

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

    public double[] CalculateIntensity(DVector4 point, DVector4 normal){
        var L = new DVector4(LightPos_InWorldSpace.X - point.X, LightPos_InWorldSpace.Y - point.Y,
            LightPos_InWorldSpace.Z - point.Z, 0);
        var distance = L.GetLength();
        L.Normalize();

        /* Фоновая составляющая */
        var I_red = Ia_Material.X * Ka_Material.X;
        var I_green = Ia_Material.Y * Ka_Material.Y;
        var I_blue = Ia_Material.Z * Ka_Material.Z;

        /* Рассеянная составляющая */
        I_red += Math.Min(1,
            Math.Max(0,
                Il_Material.X * (Kd_Material.X * DVector4.DotProduct(L, normal)) /
                (Parameters[0] * distance + Parameters[1])));
        I_green += Math.Min(1,
            Math.Max(0,
                Il_Material.Y * (Kd_Material.Y * DVector4.DotProduct(L, normal)) /
                (Parameters[0] * distance + Parameters[1])));
        I_blue += Math.Min(1,
            Math.Max(0,
                Il_Material.Z * (Kd_Material.Z * DVector4.DotProduct(L, normal)) /
                (Parameters[0] * distance + Parameters[1])));

        /* Зеркальная составляющая */
        if (DVector4.DotProduct(L, normal) > 0){
            var S = new DVector4(Center.X - point.X, Center.Y - point.Y, -1000 - point.Z, 0);
            var R = new DVector4(DVector3.Reflect((DVector3) (-L), (DVector3) normal), 0);
            S.Normalize();
            R.Normalize();

            if (DVector4.DotProduct(R, S) > 0){
                I_red += Math.Min(1,
                    Math.Max(0,
                        Il_Material.X * Ks_Material.X * Math.Pow(DVector4.DotProduct(R, S), P_Material) /
                        (Parameters[0] * distance + Parameters[1])));
                I_green += Math.Min(1,
                    Math.Max(0,
                        Il_Material.Y * Ks_Material.Y * Math.Pow(DVector4.DotProduct(R, S), P_Material) /
                        (Parameters[0] * distance + Parameters[1])));
                I_blue += Math.Min(1,
                    Math.Max(0,
                        Il_Material.Z * Ks_Material.Z * Math.Pow(DVector4.DotProduct(R, S), P_Material) /
                        (Parameters[0] * distance + Parameters[1])));
            }
        }

        I_red = Math.Min(1, I_red);
        I_green = Math.Min(1, I_green);
        I_blue = Math.Min(1, I_blue);

        return new double[]{I_red, I_green, I_blue};
    }

    public void LightCalculation(){
        if (CurVisual == Visualization.FlatShading)
            foreach (var p in polygons){
                if (!p.IsVisible) continue;
                var polMiddle = new DVector4(p.vertecies.Sum(v => v.Point_InWorldSpace.X) / 3,
                    p.vertecies.Sum(v => v.Point_InWorldSpace.Y) / 3,
                    p.vertecies.Sum(v => v.Point_InWorldSpace.Z) / 3,
                    1d);
                var result = CalculateIntensity(polMiddle, p.Normal_InWorldSpace);


                p.LightColor = Color.FromArgb((int) Math.Max(0, Math.Min(255, 255 * result[0] * MaterialColor.X)),
                    (int) Math.Max(0, Math.Min(255, 255 * result[1] * MaterialColor.Y)),
                    (int) Math.Max(0, Math.Min(255, 255 * result[2] * MaterialColor.Z)));
            }
        else if (CurVisual == Visualization.GuroShading)
            foreach (var v in vertices){
                var v_normal = new DVector4(0, 0, 0, 0);
                foreach (var p in v.polygons) v_normal += p.Normal_InWorldSpace;
                v_normal.Normalize();
                var result = CalculateIntensity(v.Point_InWorldSpace, v_normal);
                v.LightColor = Color.FromArgb((int) Math.Max(0, Math.Min(255, 255 * result[0] * MaterialColor.X)),
                    (int) Math.Max(0, Math.Min(255, 255 * result[1] * MaterialColor.Y)),
                    (int) Math.Max(0, Math.Min(255, 255 * result[2] * MaterialColor.Z)));
            }
    }

    protected DVector2 FromViewToPhysicalSpace(DVector4 point){
        var result = new DVector2();
        result.X = point.X / point.W;
        result.Y = point.Y / point.W;
        result.X = result.X * AutoScale.X +
                   Automove.X; // Преобразование координат из видового пространства в физическое
        result.Y = result.Y * AutoScale.Y + Automove.Y;
        return result;
    }

    #endregion


    protected override void OnMainWindowLoad(object sender, EventArgs args){
        // TODO: Инициализация данных

        RenderDevice.BufferBackCol = 0x20;
        ValueStorage.Font = new Font("Arial", 12f);
        ValueStorage.ForeColor = Color.Firebrick;
        ValueStorage.RowHeight = 30;
        ValueStorage.BackColor = Color.BlanchedAlmond;
        MainWindow.BackColor = Color.DarkGoldenrod;
        ValueStorage.RightColWidth = 50;
        VSPanelWidth = 400;
        VSPanelLeft = true;
        MainWindow.Size = new Size(2500, 1380);
        MainWindow.StartPosition = FormStartPosition.Manual;
        MainWindow.Location = Point.Empty;

        RenderDevice.GraphicsHighSpeed = false;

        RenderDevice.BufferBackCol = 0x20;

        RenderDevice.MouseMoveWithRightBtnDown += (s, e)
            => Offset += new DVector3(0.005 * Math.Abs(_Scale.X) * e.MovDeltaX,
                0.005 * Math.Abs(_Scale.Y) * e.MovDeltaY, 0);
        RenderDevice.MouseMoveWithLeftBtnDown += (s, e)
            => Rotation += new DVector3(0.1 * e.MovDeltaY, 0.1 * e.MovDeltaX, 0);
        RenderDevice.MouseWheel += (s, e) => Scale += new DVector3(0.001 * e.Delta, 0.001 * e.Delta, 0.001 * e.Delta);
        Create();
    }


    protected override void OnDeviceUpdate(object s, DeviceArgs e){
        if (0 != ((int) _Commands & (int) Commands.NewFigure)){
            _Commands ^= Commands.NewFigure;
            Create();
            _Commands |= Commands.FigureChange;
        }

        if (0 != ((int) _Commands & (int) Commands.FigureChange)){
            _Commands ^= Commands.FigureChange;
            Generate();
            _Commands |= Commands.Transform;
        }

        /* Обновление значений, использующихся для перевода в физ. с. к. */
        var x_min = vertices.Min(p => p.Point_InLocalSpace.X);
        var x_max = vertices.Max(p => p.Point_InLocalSpace.X);
        var y_min = vertices.Min(p => p.Point_InLocalSpace.Y);
        var y_max = vertices.Max(p => p.Point_InLocalSpace.Y);
        ViewSize.X = x_max - x_min;
        ViewSize.Y = y_max - y_min;
        Center.X = x_min + ViewSize.X / 2;
        Center.Y = y_min + ViewSize.Y / 2;
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
            var LightPosV4 = new DVector4(LightPos.X, LightPos.Y, LightPos.Z, 1);
            LightPos_InWorldSpace = PointTransform * LightPosV4;
            foreach (var p in polygons){
                p.Normal_InWorldSpace = NormalTransform * p.Normal_InLocalSpace;
                p.Normal_InWorldSpace.Normalize();
                p.IsVisible = p.Normal_InWorldSpace.Z < 0;
            }

            polygons.OrderBy(p => Math.Min(p.vertecies[0].Point_InWorldSpace.Z,
                Math.Min(p.vertecies[1].Point_InWorldSpace.Z, p.vertecies[2].Point_InWorldSpace.Z)));
            _Commands |= Commands.ShadingChange;
        }

        if (0 != ((int) _Commands & (int) Commands.ShadingChange)){
            _Commands ^= Commands.ShadingChange;
            LightCalculation();
        }

        foreach (var p in polygons){
            if (!p.IsVisible)
                continue;

            if (CurVisual == Visualization.OneColor){
                e.Surface.DrawTriangle(Color.YellowGreen.ToArgb(),
                    FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace));
            }
            else if (CurVisual == Visualization.RandomColor){
                e.Surface.DrawTriangle(p.RandomColor, FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace));
            }
            else if (CurVisual == Visualization.FlatShading){
                e.Surface.DrawTriangle(p.LightColor.ToArgb(),
                    FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace));
            }
            else if (CurVisual == Visualization.GuroShading){
                var v1 = FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace);
                var v2 = FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace);
                var v3 = FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace);
                e.Surface.DrawTriangle(p.vertecies[0].LightColor.ToArgb(), v1.X, v1.Y,
                    p.vertecies[1].LightColor.ToArgb(), v2.X, v2.Y,
                    p.vertecies[2].LightColor.ToArgb(), v3.X, v3.Y);
            }

            if (IsCarcass){
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace));
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[1].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace));
                e.Surface.DrawLine(Color.Green.ToArgb(), FromViewToPhysicalSpace(p.vertecies[2].Point_InWorldSpace),
                    FromViewToPhysicalSpace(p.vertecies[0].Point_InWorldSpace));
            }
        }

        if (IsLightSource){
            var LightInPhysicalSpace = FromViewToPhysicalSpace(LightPos_InWorldSpace);
            e.Surface.DrawTriangle(Color.Orange.ToArgb(), new DVector2(LightInPhysicalSpace.X, LightInPhysicalSpace.Y),
                new DVector2(LightInPhysicalSpace.X + 20, LightInPhysicalSpace.Y),
                new DVector2(LightInPhysicalSpace.X + 10, LightInPhysicalSpace.Y + 10));
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