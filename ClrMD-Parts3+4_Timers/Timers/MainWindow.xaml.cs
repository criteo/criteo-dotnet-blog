using Microsoft.Diagnostics.Runtime;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Timers
{
   public partial class MainWindow : Window
   {
      public MainWindow()
      {
         InitializeComponent();
      }


   #region internal helpers
   #endregion
      private string SelectDumpFile()
      {
         // select the dump file to open
         OpenFileDialog ofd = new OpenFileDialog()
         {
            DefaultExt = ".dmp",
            Filter = "Dump files (.dmp)|*.dmp",
         };

         Nullable<bool> result = ofd.ShowDialog();
         if (result == true)
         {
            if (string.IsNullOrEmpty(ofd.FileName))
               return null;
         }
         else
         {
            return null;
         }

         return ofd.FileName;
      }
      private void OpenDumpFile(string filename)
      {
         using (DataTarget dt = DataTarget.LoadCrashDump(filename))
         {
            var clr = dt.ClrVersions[0];
            try
            {
               ClrRuntime runtime = clr.CreateRuntime();

               ShowTimers(runtime);
            }
            catch (Exception x)
            {
               WriteLine("Error: " + x.Message);
            }
         }
      }
      private void ShowTimers(ClrRuntime runtime)
      {
         Dictionary<string, TimerStat> stats = new Dictionary<string, TimerStat>(64);
         int totalCount = 0;
         foreach (var timer in EnumerateTimers(runtime))
         {
            totalCount++;

            string key = string.Intern(GetTimerKey(timer));
            string line = GetTimerDetails(timer);

            TimerStat stat;
            if (!stats.ContainsKey(key))
            {
               stat = new TimerStat()
               {
                  Count = 0,
                  Line = line,
                  Period = timer.Period
               };
               stats[key] = stat;
            }
            else
            {
               stat = stats[key];
            }
            stat.Count = stat.Count + 1;
         }

         // create a summary
         WriteLine("\r\n " + totalCount.ToString() + " timers\r\n-----------------------------------------------");
         foreach (var stat in stats.OrderBy(kvp => kvp.Value.Count))
         {
            WriteLine(string.Format(
                "{0,4} | {1}",
                stat.Value.Count.ToString(),
                stat.Value.Line
            ));
         }

      }
      string GetTimerKey(TimerInfo timer)
      {
         return string.Format(
             "@{0,8} ms every {1,8} ms |  ({2}) -> {3}",
             timer.DueTime.ToString(),
             (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
             timer.StateTypeName,
             timer.MethodName
         );
      }
      string GetTimerDetails(TimerInfo timer)
      {
         return string.Format(
             "{0} @{1,8} ms every {2,8} ms |  {3} ({4}) -> {5}",
             timer.TimerQueueTimerAddress.ToString("X16"),
             timer.DueTime.ToString(),
             (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
             timer.StateAddress.ToString("X16"),
             timer.StateTypeName,
             timer.MethodName
         );
      }
      public IEnumerable<TimerInfo> EnumerateTimers(ClrRuntime runtime)
      {
         ClrHeap heap = runtime.GetHeap();
         if (!heap.CanWalkHeap)
            yield break;

         var timerQueueType = GetMscorlib(runtime).GetTypeByName("System.Threading.TimerQueue");
         if (timerQueueType == null)
            yield break;

         ClrStaticField staticField = timerQueueType.GetStaticFieldByName("s_queue");
         if (staticField == null)
            yield break;

         foreach (ClrAppDomain domain in runtime.AppDomains)
         {
            ulong? timerQueue = (ulong?)staticField.GetValue(domain);
            if (!timerQueue.HasValue || timerQueue.Value == 0)
               continue;

            // m_timers is the start of the list of TimerQueueTimer
            var currentPointer = GetFieldValue(heap, timerQueue.Value, "m_timers");

            while ((currentPointer != null) && (((ulong)currentPointer) != 0))
            {
               // currentPointer points to a TimerQueueTimer instance
               ulong currentTimerQueueTimerRef = (ulong)currentPointer;

               TimerInfo ti = new TimerInfo()
               {
                  TimerQueueTimerAddress = currentTimerQueueTimerRef
               };

               var val = GetFieldValue(heap, currentTimerQueueTimerRef, "m_dueTime");
               ti.DueTime = (uint)val;
               val = GetFieldValue(heap, currentTimerQueueTimerRef, "m_period");
               ti.Period = (uint)val;
               val = GetFieldValue(heap, currentTimerQueueTimerRef, "m_canceled");
               ti.Cancelled = (bool)val;
               val = GetFieldValue(heap, currentTimerQueueTimerRef, "m_state");
               ti.StateTypeName = "";
               if (val == null)
               {
                  ti.StateAddress = 0;
               }
               else
               {
                  ti.StateAddress = (ulong)val;
                  var stateType = heap.GetObjectType(ti.StateAddress);
                  if (stateType != null)
                  {
                     ti.StateTypeName = stateType.Name;
                  }
               }

               // decypher the callback details
               val = GetFieldValue(heap, currentTimerQueueTimerRef, "m_timerCallback");
               if (val != null)
               {
                  ulong elementAddress = (ulong)val;
                  if (elementAddress == 0)
                     continue;

                  var elementType = heap.GetObjectType(elementAddress);
                  if (elementType != null)
                  {
                     if (elementType.Name == "System.Threading.TimerCallback")
                     {
                        ti.MethodName = BuildTimerCallbackMethodName(runtime, elementAddress);
                     }
                     else
                     {
                        ti.MethodName = "<" + elementType.Name + ">";
                     }
                  }
                  else
                  {
                     ti.MethodName = "{no callback type?}";
                  }
               }
               else
               {
                  ti.MethodName = "???";
               }

               yield return ti;

               currentPointer = GetFieldValue(heap, currentTimerQueueTimerRef, "m_next");
            }
         }
      }
      private string BuildTimerCallbackMethodName(ClrRuntime runtime, ulong timerCallbackRef)
      {
         var heap = runtime.GetHeap();
         var methodPtr = GetFieldValue(heap, timerCallbackRef, "_methodPtr");
         if (methodPtr != null)
         {
            ClrMethod method = runtime.GetMethodByAddress((ulong)(long)methodPtr);
            if (method != null)
            {
               // look for "this" to figure out the real callback implementor type
               string thisTypeName = "?";
               var thisPtr = GetFieldValue(heap, timerCallbackRef, "_target");
               if ((thisPtr != null) && ((ulong)thisPtr) != 0)
               {
                  ulong thisRef = (ulong)thisPtr;
                  var thisType = heap.GetObjectType(thisRef);
                  if (thisType != null)
                  {
                     thisTypeName = thisType.Name;
                  }
               }
               else
               {
                  thisTypeName = (method.Type != null) ? method.Type.Name : "?";
               }
               return string.Format("{0}.{1}", thisTypeName, method.Name);
            }
            else
            {
               return "";
            }
         }
         else
         {
            return "";
         }
      }
      private object GetFieldValue(ClrHeap heap, ulong address, string fieldName)
      {
         var type = heap.GetObjectType(address);
         ClrInstanceField field = type.GetFieldByName(fieldName);
         if (field == null)
            return null;

         return field.GetValue(address);
      }
      private void WriteLine(string line)
      {
         tbResult.Text = tbResult.Text + line + "\r\n";
         tbResult.ScrollToEnd();
      }

      // from threadpool.cs in https://github.com/Microsoft/clrmd/tree/master/src/Microsoft.Diagnostics.Runtime/Desktop
      public ClrModule GetMscorlib(ClrRuntime runtime)
      {
         foreach (ClrModule module in runtime.Modules)
            if (module.AssemblyName.Contains("mscorlib.dll"))
               return module;

         // Uh oh, this shouldn't have happened.  Let's look more carefully (slowly).
         foreach (ClrModule module in runtime.Modules)
            if (module.AssemblyName.ToLower().Contains("mscorlib"))
               return module;

         // Ok...not sure why we couldn't find it.
         return null;
      }


   #region event handlers
   #endregion
      private void OnOpenDumpFile(object sender, RoutedEventArgs e)
      {
         string filename = tbDumpFilename.Text;

         if (string.IsNullOrEmpty(filename))
         {
            filename = SelectDumpFile();
            if (!string.IsNullOrEmpty(filename))
            {
               tbDumpFilename.Text = filename;
            }
            else
            {
               tbDumpFilename.Focus();
               return;
            }
         }

         if (!File.Exists(filename))
         {
            tbDumpFilename.Focus();
            return;
         }

         // clear the UI
         tbResult.Text = string.Empty;

         // load the dump file
         tbDumpFilename.Text = filename;
         OpenDumpFile(filename);
      }
      private void OnDumpFilenameDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
      {
         tbDumpFilename.Clear();
         OnOpenDumpFile(sender, null);
      }


      public class TimerInfo
      {
         public ulong TimerQueueTimerAddress { get; set; }
         public uint DueTime { get; set; }
         public uint Period { get; set; }
         public bool Cancelled { get; set; }
         public ulong StateAddress { get; set; }
         public string StateTypeName { get; set; }
         public ulong ThisAddress { get; set; }
         public string MethodName { get; set; }
      }

      class TimerStat
      {
         public uint Period { get; set; }
         public String Line { get; set; }
         public int Count { get; set; }
      }
   }
}
