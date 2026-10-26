using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class NativeHookTests {
 [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
 [STAThread] private static int Main() {
  try {
   SetProcessDPIAware();
   Application.EnableVisualStyles();
   var app = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","NetWatch.exe")));
   var type = app.GetType("NetWatch.AlertWindow",true);
   var flags = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
   using(var alert=(Form)Activator.CreateInstance(type,flags,null,new object[]{SystemIcons.Information,"这是一条测试提醒，未实际断开网络。",5m,true},null)) {
    var handle=alert.Handle;
    if(IsWindowVisible(handle)) throw new Exception("Test window must remain hidden");
    var monitorField=type.GetField("screenClick",flags);
    if(monitorField.GetValue(alert)!=null) throw new Exception("Hook should not exist before OnShown");
    type.GetMethod("OnShown",flags).Invoke(alert,new object[]{EventArgs.Empty});
    var monitor=monitorField.GetValue(alert);
    var hookField=monitor.GetType().GetField("hook",flags);
    if((IntPtr)hookField.GetValue(monitor)==IntPtr.Zero) throw new Exception("Windows did not install the screen-click hook");
    var hitTest=type.GetMethod("IsExitTarget",flags);
    bool nativeCloseExcluded=false;
    for(int x=alert.Bounds.Right-5;x>alert.Bounds.Right-100 && !nativeCloseExcluded;x-=5)
    for(int y=alert.Bounds.Top+3;y<alert.Bounds.Top+45;y+=4)
     if((bool)hitTest.Invoke(alert,new object[]{new Point(x,y)})) {nativeCloseExcluded=true;break;}
    if(!nativeCloseExcluded) throw new Exception("Native close button was not excluded from screen dismissal");
    Console.WriteLine("PASS Native popup X is reserved for full exit rather than screen dismissal");
    alert.Close();
    if((IntPtr)hookField.GetValue(monitor)!=IntPtr.Zero) throw new Exception("Closing did not release hook");
    if(!alert.IsDisposed) throw new Exception("Alert was not disposed");
    Console.WriteLine("PASS Native hook installs on alert activation and releases on close; no desktop window shown or mouse input injected");
   }
   return 0;
  } catch(Exception e) {Console.WriteLine("FAIL "+e);return 1;}
 }
}
