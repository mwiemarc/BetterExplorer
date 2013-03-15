﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.FileOperations;
using PipesClient;
using PipesServer;
using WindowsHelper;

namespace FileOperation {
  public partial class Form1 : Form {
    public static Guid SourceHandle = Guid.Empty;
    public List<Tuple<String,String>> SourceItemsCollection { get; set; }
    public string DestinationLocation { get; set; }
    private PipeClient _pipeClient;
    private PipeServer _pipeServer;
    public delegate void NewMessageDelegate(string NewMessage);
    public Boolean Cancel = false;
    private OperationType OPType { get; set; }

    private ManualResetEvent _block;
    private ManualResetEvent _block2;
    public IntPtr Handle;
    private int CurrentStatus = -1;
    private bool IsShown = true;
    Thread CopyThread;

    public Form1() {
      InitializeComponent();
      _block = new ManualResetEvent(false);
      _block2 = new ManualResetEvent(false);
      
      SourceItemsCollection = new List<Tuple<string, string>>();
      try {
        SourceHandle = Guid.Parse(Environment.GetCommandLineArgs().Where(c => c.StartsWith("ID:")).Single().Substring(3));
      } catch (Exception) {

      }
      Text = String.Format("FO{0}", SourceHandle);
      
      
      _pipeServer = new PipeServer();
      _pipeServer.PipeMessage += new DelegateMessage(PipesMessageHandler);
      _pipeServer.Listen("CCH" + SourceHandle.ToString());

      _pipeClient = new PipeClient();
      _pipeClient.PipeFinished += _pipeClient_PipeFinished;


    }

    void _pipeClient_PipeFinished(object sender, EventArgs e) {
      CurrentStatus = 0;
      _block2.Set();
    }
    long OldBytes = 0;
    CopyFileCallbackAction CopyCallback(ShellObject src, String dst, object state, long totalFileSize, long totalBytesTransferred) {
      //Console.WriteLine("{0}\t{1}", totalFileSize, totalBytesTransferred);
      _block.WaitOne();
      
      if (totalBytesTransferred > 0) {

       // _pipeClient.Send(totalBytesTransferred.ToString() + "|" + totalFileSize.ToString(), "DATACH" + SourceHandle.ToString());
       // if (IsShown) {
          //if (totalBytesTransferred - OldBytes >= 1024 * 1024 * 100) {
            _pipeClient.Send(totalBytesTransferred.ToString() + "|" + totalFileSize.ToString(), "DATACH" + SourceHandle.ToString());
            OldBytes = totalBytesTransferred;
            _block2.Reset();
            _block2.WaitOne();
         // }
         IsShown = false;
      //  }

        //if (totalBytesTransferred == totalFileSize) {
        //  _pipeClient.Send(totalBytesTransferred.ToString() + "|" + totalFileSize.ToString(), "DATACH" + SourceHandle.ToString());
        //  Thread.Sleep(10);
        //}
          
      } 

      if (Cancel)
        return CopyFileCallbackAction.Cancel;

      return CopyFileCallbackAction.Continue;
    }

    private void PipesMessageHandler(string message) {

     // try {
        
        if (this.InvokeRequired) {
          this.Invoke(new NewMessageDelegate(PipesMessageHandler), message);
        } else {
          char unicodeSeparator = (char)0x00;
          var lastChar = message.IndexOf(unicodeSeparator);
          var newMessage = message.Remove(lastChar);
          if (newMessage.StartsWith("OP|COPY")) {
            this.OPType = OperationType.Copy;
          }
          if (newMessage.StartsWith("INPUT|")) {
            var parts = newMessage.Replace("INPUT|", "").Split(Char.Parse("|"));
            SourceItemsCollection.Add(new Tuple<string, string>(parts[0].Trim(), parts[1].Trim()));
           
          }
          if (message.StartsWith("MM")) {
            label1.Text = "MMMMMM";
            IsShown = true;
          }
          if (newMessage.StartsWith("END FO INIT")) {
            _block.Set();

            CopyThread = new Thread(new ThreadStart(CopyFiles));
            CopyThread.IsBackground = false;
            CopyThread.Start();
            CopyThread.Join(1);

            
            
            //MessageBox.Show(SourceItemsCollection.Count.ToString());
          }
          if (message.StartsWith("COMMAND|")) {
            var realMessage = newMessage.Replace("COMMAND|", String.Empty);
            switch (realMessage) {
              case "STOP":
                this.Cancel = true;
                break;
              case "PAUSE":
                _block.Reset();
                break;
              case "CONTINUE":
                _block.Set();
                break;
              case "CLOSE":
                Close();
                break;
              default:
                break;
            }
          }
          label1.Text = message;
          _block2.Set();
        }
     // } catch (Exception ex) {

     //   Debug.WriteLine(ex.Message);
     // }


    }

    void CopyFiles() {
      CurrentStatus = 1;
      _block.WaitOne();
      foreach (var item in SourceItemsCollection) {
        OldBytes = 0;
        if (this.OPType == OperationType.Copy) {
          if (!CustomFileOperations.CopyFile(ShellObject.FromParsingName(item.Item1), item.Item2, CopyFileOptions.None, CopyCallback)) {
            int error = Marshal.GetLastWin32Error();
            if (error == 1225) {
              this.Cancel = true;
              CopyThread.Abort();
              Close();
            }
          }
        }
      }
    }

    protected override void WndProc(ref Message m) {
      base.WndProc(ref m);
    }
  }

}