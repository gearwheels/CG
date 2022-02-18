using Device = CGLabPlatform.OGLDevice;
using DeviceArgs = CGLabPlatform.OGLDeviceUpdateArgs;
using SharpGL;
using System;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CGLabPlatform;
// ==================================================================================
using CGApplication = MyApp;
using System.ComponentModel;

public abstract class MyApp : OGLApplicationTemplate<MyApp>{
    #region Классы

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex{
        public Vertex(float px, float py, float pz, float pw, byte pr, byte pg, byte pb){
            vx = px;
            vy = py;
            vz = pz;
            vw = pw;
            nx = 0;
            ny = 0;
            nz = 0;
            nw = 0;
            r = pr;
            g = pg;
            b = pb;
        }

        public void SetNorm(float nx, float ny, float nz, float nw){
            this.nx = nx;
            this.nx = ny;
            this.nx = nz;
            this.nx = nw;
        }

        public readonly float vx, vy, vz, vw;
        public float nx, ny, nz, nw;
        public readonly byte r, g, b;
    }

    private void ChangeNormale(ref Vertex vertex, DVector4 normale){
        var check = new DVector4(vertex.nx, vertex.ny, vertex.nz, 0);
        check += normale;
        vertex.nx = (float) check.X;
        vertex.ny = (float) check.Y;
        vertex.nz = (float) check.Z;
    }

    #endregion

    // TODO: Добавить свойства, поля

    [Flags]
    private enum Commands : int{
        None = 0,
        Transform = 1 << 0,
        FigureChange = 1 << 1,
        NewFigure = 1 << 3,
        ChangeProjectionMatrix = 1 << 4,
        ShadingChange = 1 << 5,
        ChangeLightPos = 1 << 6
    }

    private Commands _Commands = Commands.FigureChange;

    #region Работа с афинными преобразованиями фигуры

    #region Свойства

    private DMatrix4 _PointTransform = new DMatrix4(new[]{
        1d, 0, 0, 0,
        0, 1d, 0, 0,
        0, 0, 1d, 0,
        0, 0, 0, 1d
    });

    private DVector3 _Rotations;
    private DVector3 _Offset;
    private DVector3 _Scale;

    [DisplayNumericProperty(1, 0.1, Minimum: 1, Name: "Радиус")]
    public float Radius{
        get => Get<float>();
        set{
            if (Set<float>(value)) _Commands |= Commands.FigureChange;
        }
    }

    [DisplayNumericProperty(new[]{16d, 12d}, Minimum: 4d, Increment: 2d, Name: "Апроксимация")]
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
    public DVector3 Rotations{
        get => _Rotations;
        set{
            if (Set<DVector3>(value)){
                _Rotations = value;
                if (_Rotations.X >= 360) _Rotations.X -= 360;
                if (_Rotations.Y >= 360) _Rotations.Y -= 360;
                if (_Rotations.Z >= 360) _Rotations.Z -= 360;
                if (_Rotations.X < 0) _Rotations.X += 360;
                if (_Rotations.Y < 0) _Rotations.Y += 360;
                if (_Rotations.Z < 0) _Rotations.Z += 360;
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
                    _Rotations.X = 35;
                    _Rotations.Y = 45;
                    _Rotations.Z = 0;
                }

                UpdateTransformMatrix();
                _Commands |= Commands.Transform;
            }
        }
    }

    #endregion

    #endregion


    public enum Visualization{
        [Description("Каркасная визуализация")]
        NoPolygons,
        [Description("Одним цветом")] OneColor,
        [Description("Случайные цвета")] RandomColor,
        [Description("Метод затенения Фонга")] PhongShading
    }

    [DisplayEnumListProperty(Visualization.OneColor, "Способ отрисовки")]
    public abstract Visualization CurVisual{ get; set; }

    [DisplayCheckerProperty(false, "Выполнять анимацию")]
    public abstract bool isAnimation{ get; set; }

    [DisplayNumericProperty(1000, Minimum: 1, Increment: 1, Name: "Скорость анимации")]
    public abstract int TimeSpeed{ get; set; }


    #region Свойства освещения

    [DisplayCheckerProperty(false, "Показывать источник света")]
    public abstract bool isLightActive{ get; set; }

    [DisplayCheckerProperty(false, "Показывать нормали вершин")]
    public abstract bool isNormalActive{ get; set; }


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
            if (Set<DVector3>(value)) _Commands |= Commands.ChangeLightPos;
        }
    }

    public DVector4 LightPos_InWorldSpace;
    public uint[] LightVertexBuffer = new uint[1];
    public uint[] LightIndexBuffer = new uint[1];

    public uint[] LightIndexValues = new uint[1]{0};
    public double[] LightVertexArray;

    [DisplayNumericProperty(new[]{0.1d, 0.35d}, Minimum: 0.01d, Maximum: 100d, Increment: 0.01d, Name: "md, mk")]
    public DVector2 Parameters{
        get => Get<DVector2>();
        set{
            if (Set<DVector2>(value)) _Commands |= Commands.ShadingChange;
        }
    }

    #endregion

    #region Свойства отображения

    [DisplayNumericProperty(new[]{0d, 0d, 0d}, Minimum: 0d, Increment: 0.01d, Name: "Положение камеры")]
    public virtual DVector3 CameraPos{
        get => Get<DVector3>();
        set{
            if (Set(value)) _Commands |= Commands.ShadingChange;
        }
    }

    [DisplayNumericProperty(-1.7d, Maximum: -0.1d, Increment: 0.1d, Decimals: 2, Name: "Удаленность камеры")]
    public virtual double CameraDistance{
        get => Get<double>();
        set{
            if (Set(value)) _Commands |= Commands.Transform;
        }
    }

    [DisplayNumericProperty(60d, Maximum: 90d, Minimum: 30d, Increment: 1d, Decimals: 1, Name: "Поле зрения")]
    public virtual double FieldVision{
        get => Get<double>();
        set{
            if (Set(value)) _Commands |= Commands.ChangeProjectionMatrix;
        }
    }

    [DisplayNumericProperty(new[]{0.1d, 100d}, Maximum: 1000d, Minimum: 0.1d, Increment: 0.1d, Decimals: 2,
        Name: "Плоскости отсечения")]
    public DVector2 ClippingPlanes{
        get => _ClippingPlanes;
        set{
            if (Set(value)){
                _ClippingPlanes = value;
                if (ClippingPlanes.X > ClippingPlanes.Y) _ClippingPlanes.X = _ClippingPlanes.Y;
                _Commands |= Commands.ChangeProjectionMatrix;
            }
        }
    }

    #endregion

    #region Поля для работы с шейдерами

    private uint prog_shader;
    private uint vert_shader, frag_shader;

    private int uniform_Ka_Material, uniform_Kd_Material, uniform_Ks_Material;
    private int uniform_P_Material;
    private int uniform_Ia_Material, uniform_Il_Material;
    private int uniform_LightPos, uniform_Parameters, uniform_CameraPos;
    private int uniform_FragColor;

    private int uniform_Projection, uniform_ModelView, uniform_NormalMatrix, uniform_PointMatrix;

    private int attribute_normale, attribute_coord;

    private int uniform_time;
    private float cur_time = 0;

    #endregion

    public DVector2 _ClippingPlanes;

    public Vertex[] vertices;
    private List<List<Vertex>> LLvertices;
    public uint[] indices;
    public uint[] vertexBuffer = new uint[1];
    public uint[] indexBuffer = new uint[1];

    public DVector4[] normalPoints;
    public uint[] normalIndices;
    public uint[] normalDataBuffer = new uint[1];
    public uint[] normalIndexBuffer = new uint[1];


    public DMatrix4 pMatrix;


    #region Работа со светом

    public void UpdateLightValues(DeviceArgs e){
        var gl = e.gl;
        gl.Uniform3(uniform_Ka_Material, (float) Ka_Material.X, (float) Ka_Material.Y, (float) Ka_Material.Z);
        gl.Uniform3(uniform_Kd_Material, (float) Kd_Material.X, (float) Kd_Material.Y, (float) Kd_Material.Z);
        gl.Uniform3(uniform_Ks_Material, (float) Ks_Material.X, (float) Ks_Material.Y, (float) Ks_Material.Z);

        gl.Uniform1(uniform_P_Material, (float) P_Material);

        gl.Uniform3(uniform_Ia_Material, (float) Ia_Material.X, (float) Ia_Material.Y, (float) Ia_Material.Z);
        gl.Uniform3(uniform_Il_Material, (float) Il_Material.X, (float) Il_Material.Y, (float) Il_Material.Z);

        var LightPosV4 = new DVector4(LightPos, 1);
        LightPos_InWorldSpace = _PointTransform * LightPosV4;
        gl.Uniform3(uniform_LightPos, (float) LightPos_InWorldSpace.X, (float) LightPos_InWorldSpace.Y,
            (float) LightPos_InWorldSpace.Z);
        gl.Uniform2(uniform_Parameters, (float) Parameters.X, (float) Parameters.Y);
        gl.Uniform3(uniform_CameraPos, (float) CameraPos.X, (float) CameraPos.Y, (float) CameraPos.Z);

        gl.Uniform3(uniform_FragColor, (float) MaterialColor.X, (float) MaterialColor.Y, (float) MaterialColor.Z);

        if (isAnimation)
            gl.Uniform1(uniform_time, cur_time);
        else
            gl.Uniform1(uniform_time, -1f);
    }

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
            1, 0, 0, -_Offset.X,
            0, 1, 0, -_Offset.Y,
            0, 0, 1, _Offset.Z,
            0, 0, 0, 1
        });
        _PointTransform *= offset;


        /* Поворот */
        // По оси X
        var x_rotate = new DMatrix4(new double[]{
            1, 0, 0, 0,
            0, Math.Cos(Math.PI / 180 * _Rotations.X), Math.Sin(Math.PI / 180 * _Rotations.X), 0,
            0, -Math.Sin(Math.PI / 180 * _Rotations.X), Math.Cos(Math.PI / 180 * _Rotations.X), 0,
            0, 0, 0, 1
        });
        var y_rotate = new DMatrix4(new double[]{
            Math.Cos(Math.PI / 180 * _Rotations.Y), 0, Math.Sin(Math.PI / 180 * _Rotations.Y), 0,
            0, 1, 0, 0,
            -Math.Sin(Math.PI / 180 * _Rotations.Y), 0, Math.Cos(Math.PI / 180 * _Rotations.Y), 0,
            0, 0, 0, 1
        });
        var z_rotate = new DMatrix4(new double[]{
            Math.Cos(Math.PI / 180 * _Rotations.Z), -Math.Sin(Math.PI / 180 * _Rotations.Z), 0, 0,
            Math.Sin(Math.PI / 180 * _Rotations.Z), Math.Cos(Math.PI / 180 * _Rotations.Z), 0, 0,
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

    public uint after_top;
    public uint after_side;
    public uint after_bottom;

    private void Create(){
        approx0 = (int) Approximation[0]; // меридианы
        approx1 = (int) Approximation[1]; //параллели
        normalPoints = new DVector4[(approx0 * (approx1 - 2) / 2 + 2) * 2];
        normalIndices = new uint[(approx0 * (approx1 - 2) / 2 + 2) * 2];
    }

    private void Generate(DeviceArgs e){
        LLvertices = new List<List<Vertex>>();
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
                LLvertices[LLvertices.Count - 1].Add(new Vertex(
                    (float) (Radius * Math.Sin(sumtheta) * Math.Cos(sumphi)),
                    (float) (Radius * Math.Sin(sumtheta) * Math.Sin(sumphi)), (float) (Radius * Math.Cos(sumtheta)), 1,
                    (byte) random.Next(256), (byte) random.Next(256), (byte) random.Next(256)));
            }

            sumphi = 0;
        }

        LLvertices.Add(new List<Vertex>());
        LLvertices[LLvertices.Count - 1].Add(new Vertex(0, 0, -Radius, 1,
            (byte) random.Next(256), (byte) random.Next(256), (byte) random.Next(256))); // i - 1 ________
        LLvertices.Add(new List<Vertex>());
        LLvertices[LLvertices.Count - 1].Add(new Vertex(0, 0, Radius, 1,
            (byte) random.Next(256), (byte) random.Next(256), (byte) random.Next(256))); // i +++++


        vertices = new Vertex[(LLvertices.Count - 2) * LLvertices[0].Count + 2];
        for (var i = 0; i < vertices.Length; ++i) vertices[i] = new Vertex();

        /* Вычисление нормалей вершин верхнего основания */
        for (var i = 0; i < LLvertices[0].Count - 2; i++){
            var vec1 = new DVector4(
                LLvertices[0][i].vx - LLvertices[LLvertices.Count - 1][0].vx,
                LLvertices[0][i].vy - LLvertices[LLvertices.Count - 1][0].vy,
                LLvertices[0][i].vz - LLvertices[LLvertices.Count - 1][0].vz, 0);
            var vec2 = new DVector4(
                LLvertices[0][i + 1].vx - LLvertices[LLvertices.Count - 1][0].vx,
                LLvertices[0][i + 1].vy - LLvertices[LLvertices.Count - 1][0].vy,
                LLvertices[0][i + 1].vz - LLvertices[LLvertices.Count - 1][0].vz, 0);
            var res = vec1 * vec2; // vec2 * vec1;
            res.Normalize();
            var TmpVert = LLvertices[0][i];
            ChangeNormale(ref TmpVert, res);
            LLvertices[0][i] = TmpVert;

            TmpVert = LLvertices[LLvertices.Count - 1][0];
            ChangeNormale(ref TmpVert, res);
            LLvertices[LLvertices.Count - 1][0] = TmpVert;

            TmpVert = LLvertices[0][i + 1];
            ChangeNormale(ref TmpVert, res);
            LLvertices[0][i + 1] = TmpVert;
        }

        var vec3 = new DVector4(
            LLvertices[0][LLvertices[0].Count - 1].vx - LLvertices[LLvertices.Count - 1][0].vx,
            LLvertices[0][LLvertices[0].Count - 1].vy - LLvertices[LLvertices.Count - 1][0].vy,
            LLvertices[0][LLvertices[0].Count - 1].vz - LLvertices[LLvertices.Count - 1][0].vz, 0);
        var vec5 = new DVector4(
            LLvertices[0][0].vx - LLvertices[LLvertices.Count - 1][0].vx,
            LLvertices[0][0].vy - LLvertices[LLvertices.Count - 1][0].vy,
            LLvertices[0][0].vz - LLvertices[LLvertices.Count - 1][0].vz, 0);
        var res1 = vec3 * vec5; // vec5 * vec3;
        res1.Normalize();

        var TmpVert1 = LLvertices[0][LLvertices[0].Count - 1];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[0][LLvertices[0].Count - 1] = TmpVert1;

        TmpVert1 = LLvertices[LLvertices.Count - 1][0];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[LLvertices.Count - 1][0] = TmpVert1;

        TmpVert1 = LLvertices[0][0];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[0][0] = TmpVert1;

        /* Вычисление нормалей вершин нижнего основания */
        for (var i = 0; i < LLvertices[LLvertices.Count - 3].Count - 2; i++){
            var vec1 = new DVector4(
                LLvertices[LLvertices.Count - 3][i].vx - LLvertices[LLvertices.Count - 2][0].vx,
                LLvertices[LLvertices.Count - 3][i].vy - LLvertices[LLvertices.Count - 2][0].vy,
                LLvertices[LLvertices.Count - 3][i].vz - LLvertices[LLvertices.Count - 2][0].vz, 0);
            var vec2 = new DVector4(
                LLvertices[LLvertices.Count - 3][i + 1].vx - LLvertices[LLvertices.Count - 2][0].vx,
                LLvertices[LLvertices.Count - 3][i + 1].vy - LLvertices[LLvertices.Count - 2][0].vy,
                LLvertices[LLvertices.Count - 3][i + 1].vz - LLvertices[LLvertices.Count - 2][0].vz, 0);
            var res = vec2 * vec1; // vec1 * vec2;
            res.Normalize();
            var TmpVert = LLvertices[LLvertices.Count - 3][i];
            ChangeNormale(ref TmpVert, res);
            LLvertices[LLvertices.Count - 3][i] = TmpVert;

            TmpVert = LLvertices[LLvertices.Count - 2][0];
            ChangeNormale(ref TmpVert, res);
            LLvertices[LLvertices.Count - 2][0] = TmpVert;

            TmpVert = LLvertices[LLvertices.Count - 3][i + 1];
            ChangeNormale(ref TmpVert, res);
            LLvertices[LLvertices.Count - 3][i + 1] = TmpVert;
        }

        vec3 = new DVector4(
            LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1].vx -
            LLvertices[LLvertices.Count - 2][0].vx,
            LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1].vy -
            LLvertices[LLvertices.Count - 2][0].vy,
            LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1].vz -
            LLvertices[LLvertices.Count - 2][0].vz, 0);
        vec5 = new DVector4(
            LLvertices[LLvertices.Count - 3][0].vx - LLvertices[LLvertices.Count - 2][0].vx,
            LLvertices[LLvertices.Count - 3][0].vy - LLvertices[LLvertices.Count - 2][0].vy,
            LLvertices[LLvertices.Count - 3][0].vz - LLvertices[LLvertices.Count - 2][0].vz, 0);
        res1 = vec5 * vec3; // vec3 * vec5;
        res1.Normalize();
        TmpVert1 = LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[LLvertices.Count - 3][LLvertices[LLvertices.Count - 3].Count - 1] = TmpVert1;

        TmpVert1 = LLvertices[LLvertices.Count - 2][0];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[LLvertices.Count - 2][0] = TmpVert1;

        TmpVert1 = LLvertices[LLvertices.Count - 3][0];
        ChangeNormale(ref TmpVert1, res1);
        LLvertices[LLvertices.Count - 3][0] = TmpVert1;

        /* Вычисление нормалей вершин бокового основания */
        for (var i = 0; i < LLvertices.Count - 3; i++)
        for (var j = 0; j < LLvertices[i].Count - 1; j++){
            var vec1 = new DVector4(
                LLvertices[i + 1][j + 1].vx - LLvertices[i][j + 1].vx,
                LLvertices[i + 1][j + 1].vy - LLvertices[i][j + 1].vy,
                LLvertices[i + 1][j + 1].vz - LLvertices[i][j + 1].vz, 0);
            var vec2 = new DVector4(
                LLvertices[i][j].vx - LLvertices[i][j + 1].vx,
                LLvertices[i][j].vy - LLvertices[i][j + 1].vy,
                LLvertices[i][j].vz - LLvertices[i][j + 1].vz, 0);
            var normale1 = vec2 * vec1;
            normale1.Normalize();

            var TmpVert = LLvertices[i][j + 1];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i][j + 1] = TmpVert;

            TmpVert = LLvertices[i][j];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i][j] = TmpVert;

            TmpVert = LLvertices[i + 1][j + 1];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i + 1][j + 1] = TmpVert;

            ////////////////////////////////////////////////////////////

            vec1 = -vec1;
            vec2 = -vec2;
            var normale2 = vec2 * vec1;
            normale2.Normalize();

            TmpVert = LLvertices[i][j];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i][j] = TmpVert;

            TmpVert = LLvertices[i + 1][j + 1];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i + 1][j + 1] = TmpVert;

            TmpVert = LLvertices[i + 1][j];
            ChangeNormale(ref TmpVert, normale1);
            LLvertices[i + 1][j] = TmpVert;
        }

        /* Нормализация нормалей вершин */
        foreach (var iter in LLvertices)
        foreach (var iter1 in iter){
            var vec4 = new DVector4(iter1.nx, iter1.ny, iter1.nz, iter1.nw);
            vec4.Normalize();
            iter1.SetNorm((float) vec4.X, (float) vec4.Y, (float) vec4.Z, (float) vec4.W);
        }

        var Lindices = new List<uint>();
        /* Полигоны верхнего основания */
        Lindices.Add((uint) ((LLvertices.Count - 2) * LLvertices[0].Count + 2) -
                     1); // - 1 потому что индекс а не кол-во эл-ов 
        for (var i = LLvertices[0].Count - 1; i >= 0; --i) Lindices.Add((uint) i);

        Lindices.Add((uint) LLvertices[0].Count - 1);
        Lindices.Add((uint) ((LLvertices.Count - 2) * LLvertices[0].Count + 2) - 1);

        after_top = (uint) Lindices.Count;
        /* Боковые полигоны */
        indx = 0;
        for (var k = 0; k < LLvertices.Count - 3; ++k){
            for (var i = 0; i < LLvertices[k].Count - 1; ++i){
                Lindices.Add((uint) (indx + LLvertices[0].Count));
                Lindices.Add((uint) (indx + 1));
                ++indx;
            }

            Lindices.Add((uint) (indx + LLvertices[0].Count));
            Lindices.Add((uint) (indx + 1 - LLvertices[0].Count));
            //////////////////////////
            Lindices.Add((uint) (indx + 1));
            Lindices.Add((uint) (indx + 2 - LLvertices[0].Count));

            ++indx;
        }

        after_side = (uint) Lindices.Count;
        /* Полигоны нижнего основания */
        Lindices.Add((uint) ((LLvertices.Count - 2) * LLvertices[0].Count + 2) - 2);
        indx = (LLvertices.Count - 3) * LLvertices[0].Count;
        for (var i = 0; i < LLvertices[LLvertices.Count - 3].Count; ++i){
            Lindices.Add((uint) indx);
            ++indx;
        }

        Lindices.Add((uint) ((LLvertices.Count - 3) * LLvertices[0].Count));
        Lindices.Add((uint) ((LLvertices.Count - 2) * LLvertices[0].Count + 2) - 2);
        after_bottom = (uint) Lindices.Count;
//################################## List --> array ####################################################3
        indices = new uint[Lindices.Count];
        var i1 = 0;
        foreach (var iter in Lindices){
            indices[i1] = iter;
            ++i1;
        }

        var art_it = 0;
        foreach (var iter in LLvertices)
        foreach (var iter1 in iter){
            vertices[art_it] = iter1;
            ++art_it;
        }

        //############################################################################################
        /* Инициализация массивов для отрисовки нормалей */
        var normalLength = 0.25;
        for (var i = 0; i < vertices.Length; ++i){
            normalPoints[2 * i] = new DVector4(vertices[i].vx, vertices[i].vy, vertices[i].vz, vertices[i].vw);
            normalPoints[2 * i + 1] = new DVector4(vertices[i].vx + normalLength * vertices[i].nx,
                vertices[i].vy + normalLength * vertices[i].ny,
                vertices[i].vz + normalLength * vertices[i].nz,
                vertices[i].vw + normalLength * vertices[i].nw);
        }

        for (var i = 0; i < normalIndices.Length; ++i) normalIndices[i] = (uint) i;

        /* Загрузка буферов */
        var gl = e.gl;
        unsafe{
            /* Обработка массива вершин */
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, vertexBuffer[0]);
            fixed (Vertex* ptr = &vertices[0]){
                gl.BufferData(OpenGL.GL_ARRAY_BUFFER, vertices.Length * sizeof(Vertex), (IntPtr) ptr,
                    OpenGL.GL_STATIC_DRAW);
            }

            /* Обработка индексного массива */
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, indexBuffer[0]);
            fixed (uint* ptr = &indices[0]){
                gl.BufferData(OpenGL.GL_ELEMENT_ARRAY_BUFFER, indices.Length * sizeof(uint), (IntPtr) ptr,
                    OpenGL.GL_STATIC_DRAW);
            }

            /* Обработка массива нормалей */
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, normalDataBuffer[0]);
            fixed (DVector4* ptr = &normalPoints[0]){
                gl.BufferData(OpenGL.GL_ARRAY_BUFFER, normalPoints.Length * sizeof(DVector4), (IntPtr) ptr,
                    OpenGL.GL_STATIC_DRAW);
            }

            /* Обработка индексного массива нормалей */
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, normalIndexBuffer[0]);
            fixed (uint* ptr = &normalIndices[0]){
                gl.BufferData(OpenGL.GL_ELEMENT_ARRAY_BUFFER, normalIndices.Length * sizeof(uint), (IntPtr) ptr,
                    OpenGL.GL_STATIC_DRAW);
            }
        }
    }


    private void UpdateProjectionMatrix(DeviceArgs e) // Задание матрицы проекций
    {
        var gl = e.gl;

        gl.MatrixMode(OpenGL.GL_PROJECTION);
        pMatrix = Perspective(FieldVision, 1, ClippingPlanes.X, ClippingPlanes.Y);
        gl.LoadMatrix(pMatrix.ToArray(true));
    }

    private DMatrix4 Rotation(double x_rad, double y_rad, double z_rad){
        var x_rotate = new DMatrix4(new double[]{
            1, 0, 0, 0,
            0, Math.Cos(x_rad), -Math.Sin(x_rad), 0,
            0, Math.Sin(x_rad), Math.Cos(x_rad), 0,
            0, 0, 0, 1
        });
        var y_rotate = new DMatrix4(new double[]{
            Math.Cos(y_rad), 0, Math.Sin(y_rad), 0,
            0, 1, 0, 0,
            -Math.Sin(y_rad), 0, Math.Cos(y_rad), 0,
            0, 0, 0, 1
        });
        var z_rotate = new DMatrix4(new double[]{
            Math.Cos(z_rad), -Math.Sin(z_rad), 0, 0,
            Math.Sin(z_rad), Math.Cos(z_rad), 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        });
        return x_rotate * y_rotate * z_rotate;
    }


    public DMatrix4 ModelViewMatrix;
    public DMatrix4 NormalMatrix;

    private float[] ConvertToFloatArray(DMatrix4 Matrix){
        var MatrixF = new float[16];
        var MatrixD = Matrix.ToArray();
        for (var i = 0; i < 16; ++i) MatrixF[i] = (float) MatrixD[i];
        return MatrixF;
    }

    private void UpdateModelViewMatrix(DeviceArgs e) // Создание объектно-видовой матрицы
    {
        var gl = e.gl;

        gl.MatrixMode(OpenGL.GL_MODELVIEW);
        var deg2rad = Math.PI / 180; // Вращается камера, а не сам объект
        var cameraTransform = (DMatrix3) Rotation(deg2rad * CameraPos.X, deg2rad * CameraPos.Y, deg2rad * CameraPos.Z);
        var cameraPosition = cameraTransform * new DVector3(0, 0, CameraDistance);
        var cameraUpDirection = cameraTransform * new DVector3(0, 1, 0);

        // Мировая матрица (преобразование локальной системы координат в мировую)
        var mMatrix = _PointTransform;

        // Видовая матрица (переход из мировой системы координат к системе координат камеры)
        var vMatrix = LookAt(DMatrix4.Identity, cameraPosition, DVector3.Zero, cameraUpDirection);
        // матрица ModelView
        ModelViewMatrix = vMatrix * mMatrix;
        gl.LoadMatrix(ModelViewMatrix.ToArray(true));

        // Матрица преобразования вектора
        NormalMatrix = DMatrix3.NormalVecTransf(mMatrix);
    }

    // Построение матрицы перспективной проекции
    private static DMatrix4 Perspective(double verticalAngle, double aspectRatio, double nearPlane, double farPlane){
        var radians = verticalAngle / 2 * Math.PI / 180;
        var sine = Math.Sin(radians);
        if (nearPlane == farPlane || aspectRatio == 0 || sine == 0)
            return DMatrix4.Zero;
        var cotan = Math.Cos(radians) / sine;
        var clip = farPlane - nearPlane;
        return new DMatrix4(
            cotan / aspectRatio, 0, 0, 0,
            0, cotan, 0, 0,
            0, 0, -(nearPlane + farPlane) / clip, -(2.0 * nearPlane * farPlane) / clip,
            0, 0, -1.0, 1.0
        );
    }

    // Метод умножения матрицы на видовую матрицу, полученную из точки наблюдения
    private static DMatrix4 LookAt(DMatrix4 matrix, DVector3 eye, DVector3 center, DVector3 up){
        var forward = (center - eye).Normalized();
        if (forward.ApproxEqual(DVector3.Zero, 0.00001))
            return matrix;
        var side = (forward * up).Normalized();
        var upVector = side * forward;
        var result = matrix * new DMatrix4(
            +side.X, +side.Y, +side.Z, 0,
            +upVector.X, +upVector.Y, +upVector.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            0, 0, 0, 1
        );
        result.M14 -= result.M11 * eye.X + result.M12 * eye.Y + result.M13 * eye.Z;
        result.M24 -= result.M21 * eye.X + result.M22 * eye.Y + result.M23 * eye.Z;
        result.M34 -= result.M31 * eye.X + result.M32 * eye.Y + result.M33 * eye.Z;
        result.M44 -= result.M41 * eye.X + result.M42 * eye.Y + result.M43 * eye.Z;
        return result;
    }

    #endregion


    protected override void OnMainWindowLoad(object sender, EventArgs args){
        // TODO: Инициализация данных


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


        RenderDevice.MouseMoveWithRightBtnDown += (s, e)
            => Offset += new DVector3(0.001 * Math.Abs(_Scale.X) * e.MovDeltaX,
                0.001 * Math.Abs(_Scale.Y) * e.MovDeltaY, 0);
        RenderDevice.MouseMoveWithLeftBtnDown += (s, e)
            => Rotations += new DVector3(0.1 * e.MovDeltaY, 0.1 * e.MovDeltaX, 0);
        RenderDevice.MouseWheel += (s, e) => Scale += new DVector3(0.001 * e.Delta, 0.001 * e.Delta, 0.001 * e.Delta);

        RenderDevice.Resize += (o, eventArgs) => { _Commands |= Commands.ChangeProjectionMatrix; };

        RenderDevice.VSync = 1;

        RenderDevice.AddScheduleTask((gl, s) => {
            gl.Enable(OpenGL.GL_CULL_FACE);
            gl.CullFace(OpenGL.GL_BACK);
            gl.FrontFace(OpenGL.GL_CW);
            gl.ClearColor(0, 0, 0, 0);

            gl.Enable(OpenGL.GL_DEPTH_TEST);
            gl.DepthFunc(OpenGL.GL_LEQUAL);
            gl.ClearDepth(1.0f);
            gl.ClearStencil(0);

            gl.GenBuffers(1, vertexBuffer);
            gl.GenBuffers(1, indexBuffer);

            gl.GenBuffers(1, LightVertexBuffer);
            gl.GenBuffers(1, LightIndexBuffer);

            gl.GenBuffers(1, normalDataBuffer);
            gl.GenBuffers(1, normalIndexBuffer);
        });


        #region Загрузка и комплиция шейдера  ------------------

        RenderDevice.AddScheduleTask((gl, s) => {
            var parameters = new int[1];

            prog_shader = gl.CreateProgram();
            if (prog_shader == 0)
                throw new Exception("OpenGL Error: не удалось создать шейдерную программу");

            var load_and_compile = new Func<uint, string, uint>(
                (shader_type, shader_name) => {
                    var shader = gl.CreateShader(shader_type);
                    if (shader == 0)
                        throw new Exception("OpenGL Error: не удалось создать объект шейдера");
                    var source = HelpUtils.GetTextFileFromRes(shader_name);
                    gl.ShaderSource(shader, source);
                    gl.CompileShader(shader);

                    gl.GetShader(shader, OpenGL.GL_COMPILE_STATUS, parameters);
                    if (parameters[0] != OpenGL.GL_TRUE){
                        gl.GetShader(shader, OpenGL.GL_INFO_LOG_LENGTH, parameters);
                        var stringBuilder = new StringBuilder(parameters[0]);
                        gl.GetShaderInfoLog(shader, parameters[0], IntPtr.Zero, stringBuilder);
                        Debug.WriteLine("\n\n\n\n ====== SHADER GL_COMPILE_STATUS: ======");
                        Debug.WriteLine(stringBuilder);
                        Debug.WriteLine("==================================");
                        throw new Exception("OpenGL Error: ошибка во при компиляции " + (
                            shader_type == OpenGL.GL_VERTEX_SHADER ? "вершиного шейдера"
                            : shader_type == OpenGL.GL_FRAGMENT_SHADER ? "фрагментного шейдера"
                            : "какого-то еще шейдера"));
                    }

                    gl.AttachShader(prog_shader, shader);
                    return shader;
                });

            vert_shader = load_and_compile(OpenGL.GL_VERTEX_SHADER, "shader1.vert");
            frag_shader = load_and_compile(OpenGL.GL_FRAGMENT_SHADER, "shader2.frag");

            gl.LinkProgram(prog_shader);
            gl.GetProgram(prog_shader, OpenGL.GL_LINK_STATUS, parameters);
            if (parameters[0] != OpenGL.GL_TRUE){
                gl.GetProgram(prog_shader, OpenGL.GL_INFO_LOG_LENGTH, parameters);
                var stringBuilder = new StringBuilder(parameters[0]);
                gl.GetProgramInfoLog(prog_shader, parameters[0], IntPtr.Zero, stringBuilder);
                Debug.WriteLine("\n\n\n\n ====== PROGRAM GL_LINK_STATUS: ======");
                Debug.WriteLine(stringBuilder);
                Debug.WriteLine("==================================");
                throw new Exception("OpenGL Error: ошибка линковкой");
            }
        });

        #endregion

        #region Удаление шейдера

        RenderDevice.Closed += (s, e) => RenderDevice.AddScheduleTask((gl, _s) => {
            gl.UseProgram(0);
            gl.DeleteProgram(prog_shader);
            gl.DeleteShader(vert_shader);
            gl.DeleteShader(frag_shader);
        });

        #endregion


        #region Связывание аттрибутов и юниформ ------------------

        RenderDevice.AddScheduleTask((gl, s) => {
            /* Использующиеся в shader2.frag */

            uniform_Ka_Material = gl.GetUniformLocation(prog_shader, "Ka_Material");
            if (uniform_Ka_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Ka_Material");

            uniform_Kd_Material = gl.GetUniformLocation(prog_shader, "Kd_Material");
            if (uniform_Kd_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Kd_Material");

            uniform_Ks_Material = gl.GetUniformLocation(prog_shader, "Ks_Material");
            if (uniform_Ks_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Ks_Material");

            uniform_P_Material = gl.GetUniformLocation(prog_shader, "P_Material");
            if (uniform_P_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут P_Material");

            uniform_Ia_Material = gl.GetUniformLocation(prog_shader, "Ia_Material");
            if (uniform_Ia_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Ia_Material");

            uniform_Il_Material = gl.GetUniformLocation(prog_shader, "Il_Material");
            if (uniform_Il_Material < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Il_Material");

            uniform_LightPos = gl.GetUniformLocation(prog_shader, "LightPos");
            if (uniform_LightPos < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут LightPos");

            uniform_Parameters = gl.GetUniformLocation(prog_shader, "Parameters");
            if (uniform_Parameters < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Parameters");

            uniform_CameraPos = gl.GetUniformLocation(prog_shader, "CameraPos");
            if (uniform_CameraPos < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут CameraPos");

            /* Использующиеся в shader1.vert */
            attribute_normale = gl.GetAttribLocation(prog_shader, "Normal");
            if (attribute_normale < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Normal");

            attribute_coord = gl.GetAttribLocation(prog_shader, "Coord");
            if (attribute_coord < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Coord");

            uniform_Projection = gl.GetUniformLocation(prog_shader, "Projection");
            if (uniform_Projection < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Projection");

            uniform_ModelView = gl.GetUniformLocation(prog_shader, "ModelView");
            if (uniform_ModelView < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут ModelView");

            uniform_FragColor = gl.GetUniformLocation(prog_shader, "FragColor");
            if (uniform_FragColor < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут FragColor");

            uniform_NormalMatrix = gl.GetUniformLocation(prog_shader, "NormalMatrix");
            if (uniform_NormalMatrix < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут NormalMatrix");

            uniform_PointMatrix = gl.GetUniformLocation(prog_shader, "PointMatrix");
            if (uniform_PointMatrix < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут PointMatrix");

            uniform_time = gl.GetUniformLocation(prog_shader, "Time");
            if (uniform_time < 0)
                throw new Exception("OpenGL Error: не удалость связать аттрибут Time");
        });

        #endregion
    }

    protected override void OnDeviceUpdate(object s, DeviceArgs e){
        var gl = e.gl;
        cur_time += e.Delta * TimeSpeed / 1e6f;

        // Очиста буфера экрана и буфера глубины (иначе рисоваться будет поверх старого )
        gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT | OpenGL.GL_STENCIL_BUFFER_BIT);

        // Задание проекционной и объектно-видовой матрицы
        if (0 != ((int) _Commands & (int) Commands.ChangeProjectionMatrix)){
            _Commands ^= Commands.ChangeProjectionMatrix;
            UpdateProjectionMatrix(e);
            _Commands |= Commands.Transform;
        }

        if (0 != ((int) _Commands & (int) Commands.NewFigure)){
            _Commands ^= Commands.NewFigure;
            Create();
            _Commands |= Commands.FigureChange;
        }

        if (0 != ((int) _Commands & (int) Commands.FigureChange)){
            _Commands ^= Commands.FigureChange;
            Generate(e);
        }

        if (0 != ((int) _Commands & (int) Commands.Transform)){
            _Commands ^= Commands.Transform;
            UpdateModelViewMatrix(e);
        }

        if (0 != ((int) _Commands & (int) Commands.ChangeLightPos)) // Здесь загружаются данные света
        {
            _Commands ^= Commands.ChangeLightPos;

            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, LightVertexBuffer[0]);
            var LightPosV4 = new DVector4(LightPos, 1);
            unsafe{
                LightVertexArray = LightPosV4.ToArray();

                fixed (double* ptr = &LightVertexArray[0]){
                    gl.BufferData(OpenGL.GL_ARRAY_BUFFER, LightVertexArray.Length * sizeof(double), (IntPtr) ptr,
                        OpenGL.GL_STATIC_DRAW);
                }
            }

            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, LightIndexBuffer[0]);
            unsafe{
                fixed (uint* ptr = &LightIndexValues[0]){
                    gl.BufferData(OpenGL.GL_ELEMENT_ARRAY_BUFFER, LightIndexValues.Length * sizeof(uint), (IntPtr) ptr,
                        OpenGL.GL_STATIC_DRAW);
                }
            }

            LightPos_InWorldSpace = _PointTransform * LightPosV4;
        }

        // Задание способа визуализации
        if (CurVisual == Visualization.OneColor){
            gl.PolygonMode(OpenGL.GL_FRONT, OpenGL.GL_FILL);
            gl.Color(MaterialColor.X, MaterialColor.Y, MaterialColor.Z);
        }
        else if (CurVisual == Visualization.RandomColor){
            gl.PolygonMode(OpenGL.GL_FRONT, OpenGL.GL_FILL);
            gl.EnableClientState(OpenGL.GL_COLOR_ARRAY);
            unsafe{
                gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, vertexBuffer[0]);
                gl.ColorPointer(3, OpenGL.GL_BYTE, sizeof(Vertex), (IntPtr) (sizeof(float) * 8));
            }
        }
        else if (CurVisual == Visualization.NoPolygons){
            gl.PolygonMode(OpenGL.GL_FRONT, OpenGL.GL_LINE);
            gl.Color(MaterialColor.X, MaterialColor.Y, MaterialColor.Z);
        }
        else if (CurVisual == Visualization.PhongShading){
            gl.PolygonMode(OpenGL.GL_FRONT, OpenGL.GL_FILL);
        }

        /* Непосредственная отрисовка */
        gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, vertexBuffer[0]);
        gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, indexBuffer[0]);

        if (CurVisual == Visualization.PhongShading){
            gl.UseProgram(prog_shader);

            UpdateLightValues(e);
            gl.UniformMatrix4(uniform_ModelView, 1, true, ConvertToFloatArray(ModelViewMatrix));
            gl.UniformMatrix4(uniform_Projection, 1, true, ConvertToFloatArray(pMatrix));
            gl.UniformMatrix4(uniform_NormalMatrix, 1, true, ConvertToFloatArray(NormalMatrix));
            gl.UniformMatrix4(uniform_PointMatrix, 1, true, ConvertToFloatArray(_PointTransform));

            gl.EnableVertexAttribArray((uint) attribute_normale);
            gl.EnableVertexAttribArray((uint) attribute_coord);
            unsafe{
                gl.VertexAttribPointer((uint) attribute_normale, 4, OpenGL.GL_FLOAT, false, sizeof(Vertex),
                    (IntPtr) (4 * sizeof(float)));
                gl.VertexAttribPointer((uint) attribute_coord, 4, OpenGL.GL_FLOAT, false, sizeof(Vertex), (IntPtr) 0);
            }
        }
        else{
            gl.UseProgram(0);
            gl.EnableClientState(OpenGL.GL_VERTEX_ARRAY);
            unsafe{
                gl.VertexPointer(4, OpenGL.GL_FLOAT, sizeof(Vertex), (IntPtr) 0);
            }
        }

        gl.DrawElements(OpenGL.GL_TRIANGLE_FAN, (int) after_top, OpenGL.GL_UNSIGNED_INT, (IntPtr) 0);
        for (var i = 0; i < (approx1 - 2) / 2 - 1; ++i)
            gl.DrawElements(OpenGL.GL_TRIANGLE_STRIP, (int) (after_side - after_top) / ((approx1 - 2) / 2 - 1),
                OpenGL.GL_UNSIGNED_INT, (IntPtr) ((
                    after_top + (after_side - after_top) / ((approx1 - 2) / 2 - 1) * i) * sizeof(uint)));
        gl.DrawElements(OpenGL.GL_TRIANGLE_FAN, (int) (after_bottom - after_side), OpenGL.GL_UNSIGNED_INT,
            (IntPtr) (after_side * sizeof(uint)));

        if (CurVisual == Visualization.PhongShading){
            gl.DisableVertexAttribArray((uint) attribute_normale);
            gl.DisableVertexAttribArray((uint) attribute_coord);
            gl.UseProgram(0);
        }
        else{
            gl.DisableClientState(OpenGL.GL_VERTEX_ARRAY);
        }

        gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, 0);
        gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, 0);

        if (CurVisual == Visualization.RandomColor) gl.DisableClientState(OpenGL.GL_COLOR_ARRAY);

        if (isLightActive){
            gl.Color(0.99, 0, 0);
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, LightVertexBuffer[0]);
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, LightIndexBuffer[0]);

            gl.EnableClientState(OpenGL.GL_VERTEX_ARRAY);
            unsafe{
                gl.VertexPointer(4, OpenGL.GL_DOUBLE, sizeof(DVector4), (IntPtr) 0);
            }

            gl.PointSize(10);
            gl.DrawElements(OpenGL.GL_POINTS, 1, OpenGL.GL_UNSIGNED_INT, (IntPtr) 0);

            gl.DisableClientState(OpenGL.GL_VERTEX_ARRAY);
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, 0);
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, 0);
        }

        if (isNormalActive){
            gl.Color(0, 0, 0.99);
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, normalDataBuffer[0]);
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, normalIndexBuffer[0]);

            gl.EnableClientState(OpenGL.GL_VERTEX_ARRAY);
            unsafe{
                gl.VertexPointer(4, OpenGL.GL_DOUBLE, sizeof(DVector4), (IntPtr) 0);
            }

            gl.DrawElements(OpenGL.GL_LINES, normalPoints.Length, OpenGL.GL_UNSIGNED_INT, (IntPtr) 0);

            gl.DisableClientState(OpenGL.GL_VERTEX_ARRAY);
            gl.BindBuffer(OpenGL.GL_ARRAY_BUFFER, 0);
            gl.BindBuffer(OpenGL.GL_ELEMENT_ARRAY_BUFFER, 0);
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