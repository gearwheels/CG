// Тимофеев Алексей М8О-307Б-19 Вариант 3
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
using CGLabPlatform;
// ==================================================================================
/*
1) перемещение (сдвиг)
2) масштабирование
3) вращение
+ помимо этого
4) авто масштабирование
5) апроксимация
 */
using CGApplication = MyApp;

public abstract class MyApp : CGApplicationTemplate<CGApplication, Device, DeviceArgs>{
    // TODO: Добавить свойства, поля
    public const int width = 300; // VSPanelWidth ширина поля с кнопками

    [DisplayNumericProperty(100, 1, Minimum: 0, Maximum: 1000, Name: "Постоянная А ")]
    public abstract double A{ get; set; }

    [DisplayNumericProperty(1000, 1, Minimum: 1, Maximum: 10000, Name: "Аппроксимация ")]
    public abstract int VertexCount{ get; set; }

    [DisplayNumericProperty(2, 0.5, Minimum: 0.1, Maximum: 10, Name: "Zoom ")]
    public abstract double zoom{ get; set; }

    [DisplayNumericProperty(0, 0.05, Minimum: 0, Maximum: 2, Name: "Коэффициент поворота*(pi) ")]
    public abstract double angel{ get; set; }

    protected DVector2 Rotation(DVector2 point){
        DVector2 resPoint;
        //X = (x — x0) * cos(alpha) — (y — y0) * sin(alpha) + x0;
        //Y = (x — x0) * sin(alpha) + (y — y0) * cos(alpha) + y0;
        var cosA = Math.Cos(angel * Math.PI);
        var sinA = Math.Sin(angel * Math.PI);
        resPoint.X = (point.X - CenterFig.X) * cosA - (point.Y - CenterFig.Y) * sinA + CenterFig.X;
        resPoint.Y = (point.X - CenterFig.X) * sinA + (point.Y - CenterFig.Y) * cosA + CenterFig.Y;
        return resPoint;
    }

    public DVector2 Center;
    public DVector2 CenterFig;

    protected void CalculateCenter() // вычисление точки центра окна и точки центра фигуры 
    {
        Center = new DVector2((MainWindow.Width - width) / 2, MainWindow.Height / 2);
        CenterFig = new DVector2(Shift.X, -Shift.Y);
        CenterFig.X *= MainWindow.Height * MainWindow.Width / (WindowWidth * WindowHeight);
        CenterFig.Y *= MainWindow.Height * MainWindow.Width / (WindowWidth * WindowHeight);
        CenterFig.X += Center.X;
        CenterFig.Y += Center.Y;
    }

    protected double WindowWidth;
    protected double WindowHeight;

    protected DVector2 FromViewToPhysicalSpace(DVector2 point){
        // Преобразование координат из видового пространства в физическое
        double
            coeff; // Коэффициент вычисляющийся из длинны и ширины окна. Используется для корректировки размера граффика
        WindowWidth = 2500;
        WindowHeight = 1380;
        CalculateCenter();
        var result =
            new DVector2(point.X, -point.Y); // В физическом пространстве ось X направлена вправо, а Y направлена вниз
        coeff = MainWindow.Height * MainWindow.Width / (WindowWidth * WindowHeight);
        result.X *= coeff;
        //coeff = (base.MainWindow.Height * base.MainWindow.Width)/ (WindowHeight * WindowWidth);
        result.Y *= coeff;
        result.X += Center.X;
        result.Y += Center.Y;
        return result;
    }

    public abstract DVector2 Shift{ get; set; }

    public double delta;

    protected override void OnMainWindowLoad(object sender, EventArgs args){
        // Созданное приложение имеет два основных элемента управления:
        // base.RenderDevice - левая часть экрана для рисования
        // base.ValueStorage - правая панель для отображения и редактирования свойств

        // Пример изменения внешниго вида элементов управления (необязательный код)
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


        // Реализация управления мышкой с зажатыми левой и правой кнопкой мыши
        RenderDevice.MouseMoveWithRightBtnDown += (s, e)
            => Shift += 10 * new DVector2(e.MovDeltaX, -e.MovDeltaY);
        RenderDevice.MouseMoveWithLeftBtnDown += (s, e)
            => Shift += 0.5 * new DVector2(e.MovDeltaX, -e.MovDeltaY);

        //base.RenderDevice.MouseMoveWithMiddleBtnDown += (s, e)
        //   => angel += e.MovDeltaX;

        // Реализация управления клавиатурой
        RenderDevice.HotkeyRegister(Keys.Up, (s, e) => Shift += DVector2.UnitY);
        RenderDevice.HotkeyRegister(Keys.Down, (s, e) => Shift -= DVector2.UnitY);
        RenderDevice.HotkeyRegister(Keys.Left, (s, e) => Shift -= DVector2.UnitX);
        RenderDevice.HotkeyRegister(Keys.Right, (s, e) => Shift += DVector2.UnitX);
        RenderDevice.HotkeyRegister(KeyMod.Shift, Keys.Up, (s, e) => Shift += 10 * DVector2.UnitY);
        RenderDevice.HotkeyRegister(KeyMod.Shift, Keys.Down, (s, e) => Shift -= 10 * DVector2.UnitY);
        RenderDevice.HotkeyRegister(KeyMod.Shift, Keys.Left, (s, e) => Shift -= 10 * DVector2.UnitX);
        RenderDevice.HotkeyRegister(KeyMod.Shift, Keys.Right, (s, e) => Shift += 10 * DVector2.UnitX);

        // ... расчет каких-то параметров или инициализация ещё чего-то, если нужно
    }

    protected void DrawScaleOY(DeviceArgs e) // разметка оси Y
    {
        var tmpCenterL = new DVector2(-10, 0);
        var tmpCenterR = new DVector2(10, 0);

        for (var i = 0; i < 5; i++){
            tmpCenterL.Y += 30;
            tmpCenterR.Y += 30;
            e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(tmpCenterL * zoom + Shift),
                FromViewToPhysicalSpace(tmpCenterR * zoom + Shift));
        }

        tmpCenterL.Y = 0;
        tmpCenterR.Y = 0;
        for (var i = 0; i < 5; i++){
            tmpCenterL.Y -= 30;
            tmpCenterR.Y -= 30;
            e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(tmpCenterL * zoom + Shift),
                FromViewToPhysicalSpace(tmpCenterR * zoom + Shift));
        }
    }

    protected void DrawScaleOX(DeviceArgs e) // разметка оси Х
    {
        var tmpCenterL = new DVector2(0, -10);
        var tmpCenterR = new DVector2(0, 10);

        for (var i = 0; i < 5; i++){
            tmpCenterL.X += 30;
            tmpCenterR.X += 30;
            e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(tmpCenterL * zoom + Shift),
                FromViewToPhysicalSpace(tmpCenterR * zoom + Shift));
        }

        tmpCenterL.X = 0;
        tmpCenterR.X = 0;
        for (var i = 0; i < 5; i++){
            tmpCenterL.X -= 30;
            tmpCenterR.X -= 30;
            e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(tmpCenterL * zoom + Shift),
                FromViewToPhysicalSpace(tmpCenterR * zoom + Shift));
        }
    }

    protected override void OnDeviceUpdate(object s, DeviceArgs e){
        // TODO: Отрисовка и обновление
        var step = 2 * Math.PI / VertexCount;
        double angle = 0;
        double X, Y;
        //создаем OX, OY и разметку 
        var mOX = new DVector2(-800, 0);
        var OX = new DVector2(800, 0);
        var mOY = new DVector2(0, -800);
        var OY = new DVector2(0, 800);
        e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(mOX * zoom + Shift),
            FromViewToPhysicalSpace(OX * zoom + Shift));
        e.Surface.DrawLine(Color.Red.ToArgb(), FromViewToPhysicalSpace(mOY * zoom + Shift),
            FromViewToPhysicalSpace(OY * zoom + Shift));
        DrawScaleOY(e);
        DrawScaleOX(e);
        //создали OX, OY и разметку
        var points = new List<DVector2>();
        while (angle < 2 * Math.PI){ // просчет точек графика
            X = A * Math.Pow(Math.Cos(angle), 3) * zoom;
            Y = A * Math.Pow(Math.Sin(angle), 3) * zoom;
            points.Add(new DVector2(X, Y) + Shift);
            angle += step;
        }

        X = A * Math.Pow(Math.Cos(angle), 3) * zoom;
        Y = A * Math.Pow(Math.Sin(angle), 3) * zoom;
        points.Add(new DVector2(X, Y) + Shift);

        for (var i = 1; i < points.Count; ++i) // рисуем график 
            e.Surface.DrawLine(Color.LawnGreen.ToArgb(), Rotation(FromViewToPhysicalSpace(points[i])),
                Rotation(FromViewToPhysicalSpace(points[i - 1])));
    }
}

// ==================================================================================
public abstract class AppMain : CGApplication{
    [STAThread]
    private static void Main(){
        RunApplication();
    }
}