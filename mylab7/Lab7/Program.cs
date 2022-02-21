#define UseOpenGL // Раскомментировать для использования OpenGL
#if (!UseOpenGL)
using Device = CGLabPlatform.GDIDevice;
using DeviceArgs = CGLabPlatform.GDIDeviceUpdateArgs;
#else
using Device = CGLabPlatform.OGLDevice;
using DeviceArgs = CGLabPlatform.OGLDeviceUpdateArgs;
using SharpGL;
#endif

using System;
using System.ComponentModel;
using System.Drawing;
using CGLabPlatform;
using System.Runtime.InteropServices;
using SharpGL.Shaders;
// ==================================================================================

using CGApplication = MyApp;
using Lab7;
using System.Linq;
using System.Windows.Forms;

public abstract class MyApp : OGLApplicationTemplate<CGApplication>
{
	#region Свойства

	[DisplayNumericProperty(Default: 0.01, Minimum: 0.005, Maximum: 1.0, Increment: 0.005, Name: "dt")]
	public abstract float dt { get; set; }

	[DisplayNumericProperty(Default: new[] { -0.5d, -0.5d }, Increment: 0.05, Name: "P0")]
	public abstract DVector2 P0 { get; set; }

	[DisplayNumericProperty(Default: new[] { -0.5d, 0.5d }, Increment: 0.05, Name: "P1")]
	public abstract DVector2 P1 { get; set; }

	[DisplayNumericProperty(Default: new[] { 0.5d, 0.5d }, Increment: 0.05, Name: "P2")]
	public abstract DVector2 P2 { get; set; }

	public abstract DVector2 DefaultDIBSize { get; set; }

	private Bezier2Curve curve;
	private byte? selectedPoint;
	private const float dotRadius = 10f;

	#endregion
	protected unsafe override void OnMainWindowLoad(object sender, EventArgs args)
	{
		//RenderDevice.BufferBackCol = 0x20;
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

		//RenderDevice.GraphicsHighSpeed = false;
		
		VSPanelWidth = 268;
		ValueStorage.RightColWidth = 60;
		RenderDevice.VSync = 1;

		#region Обработчики событий мыши и клавиатуры

		RenderDevice.MouseMoveWithLeftBtnDown += (s, e) =>
		{
			if (!selectedPoint.HasValue) return;

			var w = RenderDevice.Width / 2.0;
			var h = RenderDevice.Height / 2.0;

			var ws = WindowScale();
			var wsX = RenderDevice.Width / DefaultDIBSize.X;
			var wsY = RenderDevice.Height / DefaultDIBSize.Y;

			switch (selectedPoint.Value)
			{
				case 0:
					P0 += new DVector2(e.MovDeltaX * wsX / w, -e.MovDeltaY * wsY / h) / ws;
					return;
				case 1:
					P1 += new DVector2(e.MovDeltaX * wsX / w, -e.MovDeltaY * wsY / h) / ws;
					return;
				case 2:
					P2 += new DVector2(e.MovDeltaX * wsX / w, -e.MovDeltaY * wsY / h) / ws;
					return;
			}
		};

		RenderDevice.MouseDown += (s, e) =>
		{
			var w = RenderDevice.Width / 2.0;
			var h = RenderDevice.Height / 2.0;

			var ws = WindowScale();
			var wsX = RenderDevice.Width / DefaultDIBSize.X;
			var wsY = RenderDevice.Height / DefaultDIBSize.Y;

			var hit = new DVector2(
				(e.X - w) * wsX / w,
				-(e.Y - h) * wsY / h
				) / ws;
			var area = new DVector2(
				dotRadius * wsX / w,
				dotRadius * wsY / h
				) / ws;

			if (IsHit(P0, hit, area))
			{
				selectedPoint = 0;
				return;
			}
			if (IsHit(P1, hit, area))
			{
				selectedPoint = 1;
				return;
			}
			if (IsHit(P2, hit, area))
			{
				selectedPoint = 2;
				return;
			}
		};

		RenderDevice.MouseUp += (s, e) =>
		{
			selectedPoint = null;
		};


		#endregion

		#region Инициализация OGL и параметров рендера

		RenderDevice.AddScheduleTask((gl, s) =>
		{
			// Фон
			gl.ClearColor(0, 0, 0, 0);

			// Сглаживание линий
			gl.Enable(OpenGL.GL_LINE_SMOOTH);
			gl.BlendFunc(OpenGL.GL_LINE_SMOOTH_HINT, OpenGL.GL_NICEST);
		});

		#endregion

		PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(P0)
			|| e.PropertyName == nameof(P1)
			|| e.PropertyName == nameof(P2)
			|| e.PropertyName == nameof(dt)
			)
			{
				curve = new Bezier2Curve(P0, P1, P2, dt);
			}
		};

		curve = new Bezier2Curve(P0, P1, P2, dt);

		DefaultDIBSize = new DVector2(RenderDevice.Width, RenderDevice.Height);
	}

	protected unsafe override void OnDeviceUpdate(object s, DeviceArgs e)
	{
		if (curve == null) return;

		var gl = e.gl;

		gl.Clear(
			OpenGL.GL_COLOR_BUFFER_BIT |
			OpenGL.GL_DEPTH_BUFFER_BIT |
			OpenGL.GL_STENCIL_BUFFER_BIT
			);

		gl.LoadIdentity();
		gl.MatrixMode(OpenGL.GL_PROJECTION);

		// Получаем матрицу преобразований
		var t = GetTranslateMat();
		curve.ApplyTransform(t);

		// Касательные
		gl.LineWidth(2f);
		gl.Begin(OpenGL.GL_LINE_STRIP);
		gl.Color(0.0f, 0.5f, 0.0f);
		foreach (var d in curve.Dots.Select(x => x.pointInWorld))
		{
			gl.Vertex(d.X, d.Y);
		}
		gl.End();

		// Кривая Безье
		gl.LineWidth(5f);
		gl.Begin(OpenGL.GL_LINE_STRIP);
		gl.Color(1.0f, 1.0f, 1.0f);
		foreach (var p in curve.Points.Select(x => x.pointInWorld))
		{
			gl.Vertex(p.X, p.Y);
		}
		gl.End();

		// Точки P0, P1, P2
		gl.PointSize(dotRadius);
		gl.Begin(OpenGL.GL_POINTS);
		gl.Color(1.0f, 0.0f, 0.0f);
		foreach (var d in curve.Dots.Select(x => x.pointInWorld))
		{
			gl.Vertex(d.X, d.Y);
		}

		gl.End();

		//gl.Flush();
	}

	private DMatrix3 GetTranslateMat()
	{
		// Формируем матрицу преобразований Translate
		var ws = WindowScale();

		var wsX = RenderDevice.Width / DefaultDIBSize.X;
		var wsY = RenderDevice.Height / DefaultDIBSize.Y;

		// Масштабируем при изменении размера окна
		var scaleMat = new DMatrix3(
			ws / wsX, 0, 0,
			0, ws / wsY, 0,
			0, 0, 1
			);

		return scaleMat;
	}

	private double WindowScale()
	{
		return Math.Min(
				RenderDevice.Width / DefaultDIBSize.X,
				RenderDevice.Height / DefaultDIBSize.Y
			);
	}

	private bool IsHit(DVector2 target, DVector2 hit, DVector2 area)
	{
		return
				target.X - area.X <= hit.X && hit.X <= target.X + area.X &&
				target.Y - area.Y <= hit.Y && hit.Y <= target.Y + area.Y;
	}

	// ==================================================================================
	public abstract class AppMain : CGApplication
	{[STAThread] static void Main() { RunApplication(); } }
}
