﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SQLCover.Gateway;
using SQLCover.Source;
using SQLCover.Trace;

namespace SQLCover
{
    public class CodeCoverage
    {
        private readonly DatabaseGateway _database;
        private readonly string _databaseName;
        private readonly bool _debugger;
        private readonly TraceControllerType _traceType;
        private readonly List<string> _excludeFilter;
        private readonly bool _logging;
        private readonly SourceGateway _source;
        private CoverageResult _result;

        public const short TIMEOUT_EXPIRED = -2; //From TdsEnums
        public SqlCoverException Exception { get; private set; } = null;
        public bool IsStarted { get; private set; } = false;

        private TraceController _trace;

        //This is to better support powershell and optional parameters
        public CodeCoverage(string connectionString, string databaseName) : this(connectionString, databaseName, null, false, false, TraceControllerType.Default)
        {
        }

        public CodeCoverage(string connectionString, string databaseName, string[] excludeFilter) : this(connectionString, databaseName, excludeFilter, false, false, TraceControllerType.Default)
        {
        }

        public CodeCoverage(string connectionString, string databaseName, string[] excludeFilter, bool logging) : this(connectionString, databaseName, excludeFilter, logging, false, TraceControllerType.Default)
        {
        }

        public CodeCoverage(string connectionString, string databaseName, string[] excludeFilter, bool logging, bool debugger) : this(connectionString, databaseName, excludeFilter, logging, debugger, TraceControllerType.Default)
        {
        }

        public CodeCoverage(string connectionString, string databaseName, string[] excludeFilter, bool logging, bool debugger, TraceControllerType traceType, int commandTimeout = 30) : this(databaseName, excludeFilter, logging, debugger, traceType, commandTimeout)
        {
            _database = new DatabaseGateway(connectionString, databaseName, commandTimeout);
            _source = new DatabaseSourceGateway(_database);
        }

        public CodeCoverage(IDbConnection dbConnection, string databaseName, string[] excludeFilter, bool logging, bool debugger, TraceControllerType traceType, int commandTimeout = 30) : this(databaseName, excludeFilter, logging, debugger, traceType, commandTimeout)
        {
            _database = new DatabaseGateway(dbConnection, databaseName, commandTimeout);
            _source = new DatabaseSourceGateway(_database);
        }

        private CodeCoverage(string databaseName, string[] excludeFilter, bool logging, bool debugger,
            TraceControllerType traceType, int commandTimeout = 30)
        {
            if (debugger)
                Debugger.Launch();

            _databaseName = databaseName;
            if (excludeFilter == null)
                excludeFilter = new string[0];

            _excludeFilter = excludeFilter.ToList();
            _logging = logging;
            _debugger = debugger;
            _traceType = traceType;
        }



        public bool Start()
        {
            Exception = null;
            try
            {
                _trace = new TraceControllerBuilder().GetTraceController(_database, _databaseName, _traceType);
                _trace.Start();
                IsStarted = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug("Error starting trace: {0}", ex);
                Exception = new SqlCoverException("SQL Cover failed to start.", ex);
                IsStarted = false;
                return false;
            }
        }

        private List<string> StopInternal()
        {
            var events = _trace.ReadTrace();
            _trace.Stop();
            _trace.Drop();

            return events;
        }

        public CoverageResult Stop()
        {
            if(!IsStarted)
                throw new SqlCoverException("SQL Cover was not started, or did not start correctly.");

            IsStarted = false;

            WaitForTraceMaxLatency();

            var results = StopInternal();

            GenerateResults(_excludeFilter, results);

            return _result;
        }

        private void Debug(string message, params object[] args)
        {
            if (_logging)
                Console.WriteLine(message, args);
        }

        public CoverageResult Cover(string command)
        {

        Debug("Starting Code Coverage");

            if (!Start())
            {
                throw new SqlCoverException("Unable to start the trace - errors are recorded in the debug output");

            }
            Debug("Starting Code Coverage...Done");

            Debug("Executing Command: {0}", command);

            try
            {
                _database.Execute(command); //todo read messages or rowcounts or something
            }
            catch (System.Data.SqlClient.SqlException e)
            {
                if (e.Number == -2)
                {
                    throw;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception running command: {0} - error: {1}", command, e.Message);
            }

            Debug("Executing Command: {0}...done", command);
            WaitForTraceMaxLatency();
            Debug("Stopping Code Coverage");
            try
            {
                var rawEvents = StopInternal();
                Debug("Stopping Code Coverage...done");

                Debug("Getting Code Coverage Result");
                GenerateResults(_excludeFilter, rawEvents);
                Debug("Getting Code Coverage Result..done");
            }
            catch (Exception e)
            {
                throw new SqlCoverException("Exception gathering the results", e);
            }

            return _result;
        }

        public CoverageResult CoverExe(string exe, string args, string workingDir = null)
        {
            try
            {
                Debug("Starting Code Coverage");

                Start();
                Debug("Starting Code Coverage...done");

                Debug("Executing Command: {0} {1} {2}", workingDir, exe, args);
                RunProcess(exe, args, workingDir);
                Debug("Executing Command: {0} {1} {2}...done", workingDir, exe, args);
                WaitForTraceMaxLatency();
                Debug("Stopping Code Coverage");
                var rawEvents = StopInternal();
                Debug("Stopping Code Coverage...done");

                Debug("Getting Code Coverage Result");
                GenerateResults(_excludeFilter, rawEvents);
                Debug("Getting Code Coverage Result..done");
                
            }
            catch (Exception e)
            {
                Debug("Exception running code coverage: {0}\r\n{1}", e.Message, e.StackTrace);
            }

            return _result;
        }

        private static void WaitForTraceMaxLatency()
        {
            Thread.Sleep(1000); //max distpatch latency!
        }

        private void RunProcess(string exe, string args, string workingDir)
        {
            var si = new ProcessStartInfo();
            si.FileName = exe;
            si.Arguments = args;
            si.UseShellExecute = false;
            si.WorkingDirectory = workingDir;

            var process = Process.Start(si);
            process.WaitForExit();
        }


        private void GenerateResults(List<string> filter, List<string> xml)
        {
            var batches = _source.GetBatches(filter);
            _result = new CoverageResult(batches, xml, _databaseName, _database.DataSource());
        }

        public CoverageResult Results()
        {
            return _result;
        }
    }
}
