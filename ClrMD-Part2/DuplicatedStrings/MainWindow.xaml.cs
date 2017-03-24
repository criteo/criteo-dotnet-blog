using Microsoft.Diagnostics.Runtime;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace DuplicatedStrings
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

               ShowDuplicatedStrings(runtime, 10);
            }
            catch (Exception x)
            {
               WriteLine("Error: " + x.Message);
            }
         }
      }
      private void ShowDuplicatedStrings(ClrRuntime runtime, int minCountThreshold)
      {
         var heap = runtime.GetHeap();
         var strings = ComputeDuplicatedStrings(heap);

         if (strings == null)
         {
            WriteLine("Impossible to enumerate strings");
            return;
         }

         int totalSize = 0;

         // sort by size taken by the instances of string
          var query = strings
              .Where(s => s.Value > minCountThreshold)
              .Select(e => new
              {
                  Count = e.Value,
                  Size = 2*e.Value*e.Key.Length,
                  Key = e.Key
              })
              .OrderBy(ai => ai.Size);

         foreach (var aggregatedInfo in query)
         {
            WriteLine(string.Format(
                "{0,8} {1,12} {2}",
                aggregatedInfo.Count,
                aggregatedInfo.Size,
                aggregatedInfo.Key.Replace("\n", "## ").Replace("\r", " ##")
                ));
            totalSize += aggregatedInfo.Size;
         }

         WriteLine("-------------------------------------------------------------------------");
         WriteLine(string.Format("         {0,12} MB", totalSize / (1024 * 1024)));

      }
      private Dictionary<string, int> ComputeDuplicatedStrings(ClrHeap heap)
      {
         // do nothing if in the middle of a gargabe collection
         if (!heap.CanWalkHeap)
            return null;

         Dictionary<string, int> strings = new Dictionary<string, int>();
         foreach (var address in heap.EnumerateObjectAddresses())
         {
            try
            {
               var objType = heap.GetObjectType(address);
               if (objType == null)  // in case of memory corruption
                  continue;

               // count only strings
               if (objType.Name != "System.String")
                  continue;

               var obj = objType.GetValue(address);
               string s = obj as string;
               if (!strings.ContainsKey(s))
               {
                  strings[s] = 0;
               }

               strings[s] = strings[s] + 1;
            }
            catch (Exception x)
            {
               WriteLine(x.ToString());
               // some InvalidOperationException might occur sometimes
            }
         }

         return strings;
      }
      private void WriteLine(string line)
      {
         tbResult.Text = tbResult.Text + line + "\r\n";
         tbResult.ScrollToEnd();
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
   }
}
