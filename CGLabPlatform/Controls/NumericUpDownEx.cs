using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CGLabPlatform{
    [DesignerCategory("")]
    public class NumericUpDownEx : NumericUpDown{
        [Bindable(true)]
        public new decimal Value{
            get => base.Value;
            set{
                base.Value = Math.Min(Math.Max(Minimum, value), Maximum);
                if (base.Value == value)
                    return;
                var binding = DataBindings["Value"];
                if (binding == null)
                    return;
                var updmode = binding.ControlUpdateMode;
                binding.ControlUpdateMode = ControlUpdateMode.Never;
                binding.WriteValue();
                binding.ControlUpdateMode = updmode;
            }
        }

        // Исправление бага с изменением значения при прокрутки колесика мышки
        protected override void OnMouseWheel(MouseEventArgs e){
            var hme = e as HandledMouseEventArgs;
            if (hme != null)
                hme.Handled = true;

            if (e.Delta > 0)
                UpButton();
            else if (e.Delta < 0)
                DownButton();

            Refresh();
        }
    }
}