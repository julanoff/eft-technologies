/*                                                             
 Copyright (c) 1999 - 2012 by EFT Technologies, Inc.
 All rights reserved.

 This Software is confidential and proprietary to 
 EFT Technologies and it is protected by U.S. copyright law, 
 other national copyright laws, and international treaties.
 The Software may not be disclosed or reproduced in whole or in part in any manner to 
 any third party without the express prior written consent of 
 EFT Technologies, Inc.                                      
                                                                
 This Software and its related Documentation are proprietary
 and confidential material of EFT Technologies, Inc.
*/

/*
Version & revision
1.0 JN 2013 Added new GBS source handling. Changes in FindOrgText() function.
            Added replacement of sender ref for GBS in PutMessage()
1.1 JN Mar-8-2013  Get change reference from Simulator Control. If it is N then use it. Otherwise it is a default Change REF.
    JN Mar-21-2013  Add support for connstring.xml file. It consolidates all connection strings in on file
1.2 JN Jun-13-2013  Added check for m_orig_amt. Traps with invalid case. 
1.3 JN Jul-09-2013  Catch/handle incorrect ref file.
1.4 JN Jul-31-2013  Deal with open cursors issue. Close reader and handle 'open cursors exceed' problem.
1.5 JN Sep-24-2013  Add support for SEPA handling
1.6 JN Oct-18-2013  Added check for msg_status prior doing full compare. In case they are different show only 1 diff in msg_status
    JN Jan-15-2015  Add check for DB type before attempting to resolve aliases for connection string.
    JN Jan-20-2015  Fix bug in Putmessage to trim the src(sometimes it is 2 ch). 
    		    Added support for RCX TI source for Citi.
2.0 JN Nov-10-2015  Make SP version out of GPP one.
*/

//  to do: Find SRC for FED and CHIPS. Update putmessage routine


using System;
using System.IO;
using System.Text;
using System.Data;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using Simulator.DBLibrary;
using System.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Simulator.BackEndSubLib;
using Simulator.SimLog;
using Simulator.EventLogger;
using Simulator.FTCompare;

namespace Simulator.FTFeedDll

{
	public class FTFeedDll
	{
		private DBAccess m_Connection;
		private DBAccess m_Connection1;
		private DBAccess m_Connection2;
		private OracleConnection m_OraConnection;
		private OracleConnection m_TrgConnection;

		private string m_Area;
		private ArrayList m_Sql;
		private string m_Reffile;
		private long m_AmtDelta;
		private string m_Alg;
		private string m_Descr;
		private long m_timeDelta;
		private int m_Sqlno=0;
		private string m_src_connection_string;
		private string m_trg_connection_string;
		private string m_dbtype;
		private string m_trgdbtype;
		private string m_docompare;
		private string m_doreplay;
		private string m_fromdb;
		private string m_todb;
		private string m_script;
		private string m_prefix;
		private string m_table;
		private string m_sepa;
		private string m_cref;
		private string m_client;
		private string m_vendor;
		private Hashtable m_sources;
		Loader ldr;
		bool Show;
		private string XmlText;
		private string Pkey;
		private SimulatorLog _simLog = null;
		private	bool ChangeRef = true;
		private	bool LookbyRef = true;
	  FileStream file;
	 	StreamWriter sWrt;
	  private int m_MsgRpt;
	  private string m_orig_ref="";
		private string m_orig_amt ="";
		private string m_orig_curr="";



		public FTFeedDll (string xmlstr, string pkey, string vendor, string client)
		{
			this.XmlText = xmlstr;
			this.Pkey = pkey;
			this.m_vendor = vendor;
			this.m_client = client;
		}
		
		public void FTFeed()
		{
			BackEndSubs util = new BackEndSubs();
			_simLog = new SimulatorLog("FTFeeder");
			FTCompareCl compit = new FTCompareCl();
		
			ldr = new Loader();
			DateTime FileValueDate = DateTime.MinValue;
			DateTime NewValueDate = DateTime.MinValue;
			string tmp="";
			int NoDiff = -1;
			// CHANGE THIS: Test on PC should be = "SQL", otherwise ORA
			string SimTrgDB = System.Environment.GetEnvironmentVariable("SIMTRGDB");
			if (SimTrgDB == "SQL")
				m_trgdbtype="SQL";
			else
				m_trgdbtype="ORA";
		
			m_Sql = new ArrayList();
			XmlDocument xmlDoc = new XmlDocument();
		
			xmlDoc.LoadXml(XmlText);
		
			XmlNodeList nodes = xmlDoc.SelectNodes("ourxml");
		
			foreach(XmlNode node in nodes)
			{
				try {m_src_connection_string = node.SelectSingleNode("src_connection_string").InnerText; }
				catch {m_src_connection_string = ""; }
				try {m_trg_connection_string = node.SelectSingleNode("trg_connection_string").InnerText; }
				catch {m_trg_connection_string = ""; }
				try {m_dbtype = node.SelectSingleNode("conndb").InnerText; }
				catch {m_dbtype = "";}
				try {m_Area = node.SelectSingleNode("area").InnerText;}
				catch {m_Area = "";}
				try {m_Descr = node.SelectSingleNode("description").InnerText;}
				catch {m_Descr = "";}
				string sh="";
				try {sh = node.SelectSingleNode("show").InnerText;}
				catch { sh = "N"; }
				if (sh=="Y")
					Show = true;
				else
					Show = false;
				try {m_Reffile = node.SelectSingleNode("reffile").InnerText;}
				catch {m_Reffile = "";}
		
				try {m_docompare = node.SelectSingleNode("docompare").InnerText;}
				catch {m_docompare = "";}
		
				try {m_sepa = node.SelectSingleNode("sepa").InnerText;}
				catch {m_sepa = "";}
		
				m_cref="";
				try {m_cref = node.SelectSingleNode("changeref").InnerText;}
				catch {m_cref = "";}
				if (m_cref == "N")
				{
					ChangeRef = false;
					LookbyRef = false;
				}
				else
				{
					ChangeRef = true;
					LookbyRef = true;
				}
		
				try {m_prefix = node.SelectSingleNode("prefix").InnerText;}
				catch {m_prefix = "";}
		
				try {m_doreplay = node.SelectSingleNode("doreplay").InnerText;}
				catch {m_doreplay = "";}
		
				try {m_fromdb = node.SelectSingleNode("fromdatabase").InnerText;}
				catch {m_fromdb = "";}
		
				try {m_todb = node.SelectSingleNode("todatabase").InnerText;}
				catch {m_todb = "";}
		
				try {m_script = node.SelectSingleNode("scripttmpl").InnerText;}
				catch {m_script = "";}
		
				try {m_table = node.SelectSingleNode("cmptable").InnerText;}
				catch {m_table = "";}
		
				try {tmp = node.SelectSingleNode("amtdelta").InnerText;}
				catch {tmp = "";}
				if (tmp.Length == 0)
				{
					m_AmtDelta = 0;
				}
				else
				{
					m_AmtDelta = util.amtStringToPennies(tmp, ".");
					if (m_AmtDelta == 0)
					{
						ReportError("FTFeed: Incorrect format of Amtdelta. Exiting.", 2);
						return;
					}
				}
				try {m_Alg = node.SelectSingleNode("algorithm").InnerText;}
				catch {m_Alg = "";}
				if ((m_Alg.Length > 0) && (m_Alg !="REF") && (m_Alg != "AMT") )
				{
					ReportError("FTFeed: Incorrect format of Algorithm (REF or AMT). Exiting.",2);
					return;
				}
				try {tmp = node.SelectSingleNode("filevaldate").InnerText;}
				catch {tmp = "";}
				FileValueDate = DateTime.MinValue;
				if (tmp.Length > 0)
				{
					try { FileValueDate = Convert.ToDateTime(tmp); }
					catch
					{
						ReportError("FTFeed: Incorrect format of filevaldate. Exiting.", 2);
						return;
					}
				}
				try {tmp = node.SelectSingleNode("newvaldate").InnerText;}
				catch {tmp = "";}
				NewValueDate = DateTime.MinValue;
				if (tmp.Length > 0)
				{
					try { NewValueDate = Convert.ToDateTime(tmp); }
					catch
					{
						ReportError("FTFeed: Incorrect format of newvaldate. Exiting.",2);
						return;
					}
				}
				try {tmp = node.SelectSingleNode("sql").InnerText.TrimStart();}
				catch
				{
					ReportError("FTFeed: SQL command is missing. Exiting.",2);
					return ;
				}
		
				string[] delimiter = new string[] { "\r\n" };
				string[] rst = tmp.Split(delimiter, StringSplitOptions.None);
				foreach (string s in rst)
				{
					m_Sql.Add(s);
					m_Sqlno ++;
				}
			}
		
			// Check if it is a uniquename (alias). In this case go to connstring.xml and figure the real connection string.
			if ( (m_dbtype=="ORA") &&
			     ( (m_src_connection_string.ToUpper().IndexOf("DATA SOURCE=") == -1) ||
			       (m_trg_connection_string.ToUpper().IndexOf("DATA SOURCE=") == -1) ) )
			{
				string f_name = string.Format("c:\\Simulator\\{0}\\feed\\connstring.xml", m_Area);
				DataSet srctable = new DataSet();
				srctable.ReadXml(f_name);
				foreach (DataTable table in srctable.Tables)
				{
					foreach (DataRow row in table.Rows)
					{
						if ( (row["type"].ToString().ToLower() == "src") && (row["uniquename"].ToString() == m_src_connection_string) )
							m_src_connection_string = row["connection_string"].ToString().TrimEnd();
						if ( (row["type"].ToString().ToLower() == "trg") && (row["uniquename"].ToString() == m_trg_connection_string) )
							m_trg_connection_string = row["connection_string"].ToString().TrimEnd();
					}
				}
				if ( (m_src_connection_string.ToUpper().IndexOf("DATA SOURCE=") == -1) ||
				     (m_trg_connection_string.ToUpper().IndexOf("DATA SOURCE=") == -1) )
				{
					ReportError("FTFeed. Connection String is incorrect. Exiting.",2);
					return ;
				}
			}
		
			if (Show)
				Console.WriteLine("Connecting to  " + m_Area);
		
			// Deal with value dates...
			if ( !(NewValueDate.Equals(DateTime.MinValue))  && !(FileValueDate.Equals(DateTime.MinValue)))
			{
				m_timeDelta = NewValueDate.Ticks - FileValueDate.Ticks;
			}
		
			m_Connection=new DBAccess();
			m_Connection.Connect(true,m_Area);
		
			m_Connection2=new DBAccess();
			m_Connection2.Connect(true,m_Area);
		
			GetLineContext();
			string Cmd="";
		
			if (m_doreplay == "Y")
			{
				Cmd = string.Format("update BatchDescr set BatchStatus = 'Replay Started', NoReplay = 0 where BatchId = '{0}'", Pkey);
				//Console.WriteLine("repl comma - " + Cmd);
				m_Connection2.Execute(Cmd,true);
		
				file = new FileStream(m_Reffile, FileMode.Create, FileAccess.Write);
				sWrt = new StreamWriter(file);
		
				if (m_dbtype == "ORA")
				for (int i = 0; i < m_Sqlno; i++)
				{
					if (!GetMsgsFromORA(i))
					{
						sWrt.Flush();
						sWrt.Close();
						return ;
					}
				}
				else
				{
					m_Connection1=new DBAccess();
					m_Connection1.Connect(true,m_Area);
					for (int i = 0; i < m_Sqlno; i++)
					{
						if (!GetMsgsFromSQL(i))
						{
							sWrt.Flush();
							sWrt.Close();
							return;
						}
					}
				}
				sWrt.Flush();
				sWrt.Close();
			}
		
			if (m_docompare!="Y")
			{
				Cmd = string.Format("update BatchQ set ProcessState = 'P'," +
				" LoadTime = '{0}' where " +
				"Pk = {1}",  DateTime.Now, Pkey);
				m_Connection2.Execute(Cmd,true);
				Cmd = string.Format ("update BatchDescr set TimeEnd='{1}', BatchStatus = 'Replay Finished', NoDiffs = 0 where BatchId = '{0}'",Pkey, DateTime.Now);
				m_Connection2.Execute(Cmd,true);
				return;
			}
		
			if (m_table.Length==0)
			{
				ReportError("FTFeed: Compare table is missing. Exiting.",2);
				return;
			}
			if (m_fromdb.Length==0)
			{
				ReportError("FTFeed: From DB is missing. Exiting.",2);
				return;
			}
			if (m_todb.Length==0)
			{
				ReportError("FTFeed: To DB is missing. Exiting.",2);
				return;
			}
		
			// now comparing part
			FileStream infile;
			StreamReader sRdr;
			try
			{
				infile = new FileStream(m_Reffile, FileMode.Open, FileAccess.Read);
				sRdr = new StreamReader(infile);
			}
			catch
			{
				string err = "FTFeedDll: Reffile " + m_Reffile + " is missing. Exiting.";
				ReportError(err,2);
				return;
			}
		
			string srcMid="";
			if ( (m_OraConnection == null) && (m_dbtype=="ORA") )
			{
				m_OraConnection=new OracleConnection();
				m_OraConnection.ConnectionString = m_src_connection_string;
				m_OraConnection.Open();
			}
			if ( (m_TrgConnection == null) && (m_trgdbtype=="ORA") )
			{
				m_TrgConnection=new OracleConnection();
				m_TrgConnection.ConnectionString = m_trg_connection_string;
				m_TrgConnection.Open();
			}
			if (m_Connection == null)
			{
				m_Connection=new DBAccess();
				m_Connection.Connect(true,m_Area);
			}
			if (m_dbtype=="SQL")
			{
				m_src_connection_string = m_Connection.GetConnString(m_Area);
			}
			if (m_trgdbtype=="SQL")
			{
				m_trg_connection_string = m_Connection.GetConnString(m_Area);
			}
		
			//Check if first msg appeared in Target DB
		
			srcMid = sRdr.ReadLine();
			if (!LookbyRef)	// We have to find orig_ref, amt & curr from the Source
			{
				if (m_dbtype == "ORA")
				{
					OracleCommand command = m_OraConnection.CreateCommand();
					if (m_vendor=="FTS")
						command.CommandText = string.Format("select p_orig_instr_id, p_orig_sttlm_amt, p_orig_sttlm_ccy from {0}minf a where p_mid='{1}'", m_prefix, srcMid );
					else
						command.CommandText = string.Format("select orig_reference, orig_amount, orig_currency from {0}mif a where mid='{1}'", m_prefix, srcMid );
		
					OracleDataReader reader;
					try { reader = command.ExecuteReader(); }
					catch (Exception ex)
					{
						ReportError(string.Format("FTFeedDll: 23. Oracle error: {0},",ex.Message),2);
						return;
					}
					if (reader.Read())
					{
						if ( !reader.IsDBNull(0) )
						  if (m_vendor=="FTS")
								m_orig_ref = ((string)reader["P_ORIG_INSTR_ID"]);
							else
								m_orig_ref = ((string)reader["ORIG_REFERENCE"]); 
						else
							m_orig_ref ="";
		
						if ( !reader.IsDBNull(1) )
							m_orig_amt = reader.GetDecimal(1).ToString();
						else
							m_orig_amt = "";
		
						if ( !reader.IsDBNull(2) )
						  if (m_vendor=="FTS")
								m_orig_curr = ((string)reader["P_ORIG_STTLM_CCY"]);
							else
								m_orig_curr = ((string)reader["ORIG_CURRENCY"]); 
						else
							m_orig_curr = "";
					}
					if (Show)
						Console.WriteLine ("Looking for ref - " + m_orig_ref + " amt - " + m_orig_amt + " curr - " + m_orig_curr + " mid - " + srcMid);
					reader.Close();
					command.Dispose();
				}
				else	// SQL
				{
				  if (m_vendor=="FTS")
						Cmd = string.Format("select p_orig_instr_id,p_orig_sttlm_amt,p_orig_sttlm_ccy from {0}minf where p_mid='{1}'",m_prefix, srcMid);
					else
		    		Cmd = string.Format("select orig_reference,orig_amount,orig_currency from {0}mif where mid='{1}'",m_prefix, srcMid); 
					try { m_Connection.OpenDataReader(Cmd); }
					catch (Exception ex)
					{
						ReportError(string.Format("FTFeedDll: SQL error: {0},",ex.Message),2);
						return;
					}
					if (m_Connection.SQLDR.Read())
					{
						m_orig_ref = (m_Connection.SQLDR[0].ToString().Trim());
						m_orig_amt = (m_Connection.SQLDR[1].ToString().Trim());
						m_orig_curr = (m_Connection.SQLDR[2].ToString().Trim());
		
					}
					if (Show)
						Console.WriteLine ("Looking for ref - " + m_orig_ref + " amt - " + m_orig_amt + " curr - " + m_orig_curr + " mid - " + srcMid);
					m_Connection.CloseDataReader();
				}
			}
		
			int cnt=0;
			do
			{
				if (m_trgdbtype == "ORA")  // look for mid in mif and check the status
				{
					if (cnt > 10)	//30 sec
					{
						ReportError(string.Format("FTFeedDll: Msgs are not coming over. Batch Aborted."),2);
						return;
					}
					OracleCommand command = m_TrgConnection.CreateCommand();
					if (LookbyRef)
						if (m_vendor=="FTS")
							command.CommandText = string.Format("select p_msg_sts, p_mid from minf a where p_orig_instr_id='{0}'", srcMid);
						else
							command.CommandText = string.Format("select msg_status, mid from mif a where orig_reference='{0}'", srcMid);
					else
						if (m_vendor=="FTS")
							command.CommandText = string.Format("select p_msg_sts, p_mid from minf a where p_orig_instr_id='{0}' and p_orig_sttlm_amt='{1}' and p_orig_sttlm_ccy='{2}'", m_orig_ref,m_orig_amt,m_orig_curr);
						else
							command.CommandText = string.Format("select msg_status, mid from mif a where orig_reference='{0}' and orig_amount='{1}' and orig_currency='{2}'", m_orig_ref,m_orig_amt,m_orig_curr);
					if (Show)
						Console.WriteLine ("try # " + cnt + " cmd to look for src mid - " + command.CommandText);
		
					OracleDataReader reader;
					try { reader = command.ExecuteReader(); }
					catch (Exception ex)
					{
						ReportError(string.Format("FTFeedDll: 21. Oracle error: {0},",ex.Message),2);
						return;
					}
					if (reader.Read())
					{
						string mid="";
						if (m_vendor=="FTS")
						{
							string stat = ((string)reader["P_MSG_STS"]);
							mid = ((string)reader["P_MID"]);
						}
						else
						{
							string stat = ((string)reader["MSG_STATUS"]); 
							mid = ((string)reader["MID"]); 
						}
						if (mid != "")
							break;
					}
					else //Mid not found. repeat after 3 sec
					{
						cnt++;
						System.Threading.Thread.Sleep(3000);       // sleep 3 sec.
						continue;
					}
					foreach (OracleParameter param in command.Parameters)
					{
						param.Dispose();
					}
					reader.Close();
					command.Dispose();
				}
				else	break;	// means SQL and we are testing
			}
			while(true);
		
			Cmd = string.Format("update BatchDescr set BatchStatus = 'Compare Started', NoDiffs = 0 where BatchId = '{0}'", Pkey);
			m_Connection2.Execute(Cmd,true);
		
			if (Show)
				Console.WriteLine("Compare started for Batch - " + Pkey + "  File - " + m_Reffile);
		
			sRdr.BaseStream.Position = 0;
			sRdr.DiscardBufferedData();
			srcMid="";
			m_MsgRpt=0;
			m_orig_ref="";
			m_orig_amt ="";
			m_orig_curr="";
			do
			{
				srcMid = sRdr.ReadLine();
				int MidIndex=0;
		
				List<string> MsgCls = new List<string>();
				List<string> SrcMsgStatus = new List<string>();
				List<string> TrgMsgStatus = new List<string>();
				List<string> SrcMids = new List<string>();
				List<string> TrgMids = new List<string>();
		
				if (srcMid != null)
				{
					if (m_dbtype == "ORA")  // look for Src mid and see if it OPI class
					{
						OracleCommand command = m_OraConnection.CreateCommand();
						if (m_vendor=="FTS")
							command.CommandText = string.Format("select p_msg_class,p_mid,p_orig_instr_id,p_orig_sttlm_amt,p_orig_sttlm_ccy,p_msg_sts from {0}minf where p_mid='{1}'",m_prefix, srcMid);
		    		else
		    			command.CommandText = string.Format("select msg_class,mid,orig_reference,orig_amount,orig_currency,msg_status from {0}mif where mid='{1}'",m_prefix, srcMid); 
						OracleDataReader reader;
						try { reader = command.ExecuteReader(); }
						catch (Exception ex)
						{
							ReportError(string.Format("FTFeedDll: 1. Oracle error: {0},",ex.Message),1);
							m_MsgRpt++;
		
							/* in case of : ORA-00604, ORA-01000
							ORA-00604: error occurred at recursive SQL level 1
							ORA-01000: maximum open cursors exceeded,
							*/
							if ( (ex.Message.IndexOf("ORA-00604") != -1) ||
									 (ex.Message.IndexOf("ORA-01000") != -1) )
							{
								if ( m_dbtype=="ORA" )
								{
									m_OraConnection.Close();
									m_OraConnection.Dispose();
									m_OraConnection=new OracleConnection();
									m_OraConnection.ConnectionString = m_src_connection_string;
									m_OraConnection.Open();
								}
								if ( m_trgdbtype=="ORA" )
								{
									m_TrgConnection.Close();
									m_TrgConnection.Dispose();
									m_TrgConnection=new OracleConnection();
									m_TrgConnection.ConnectionString = m_trg_connection_string;
									m_TrgConnection.Open();
								}
								command.Dispose();
							}
							continue;
						}
						if (reader.Read())
						{
							if (m_vendor=="FTS")
							{
								MsgCls.Add ( (string)reader["P_MSG_CLASS"]);
								SrcMsgStatus.Add ( (string)reader["P_MSG_STS"]);
								SrcMids.Add ( (string)reader["P_MID"]);
								if ( !reader.IsDBNull(2) )
									m_orig_ref = (string)reader["P_ORIG_INSTR_ID"];
								else
									m_orig_ref = "";
			
								if ( !reader.IsDBNull(3) )
									m_orig_amt = reader.GetDecimal(3).ToString();
								else
									m_orig_amt = "";
			
								if ( !reader.IsDBNull(4) )
									m_orig_curr = (string)reader["P_ORIG_STTLM_CCY"];
								else
									m_orig_curr ="";
							}
							else
							{
								MsgCls.Add ( (string)reader["MSG_CLASS"]);
								SrcMsgStatus.Add ( (string)reader["MSG_STATUS"]);
    						SrcMids.Add ( (string)reader["MID"]); 
    						if ( !reader.IsDBNull(2) )
    							m_orig_ref = (string)reader["ORIG_REFERENCE"];
    						else
    							m_orig_ref = "";
    						if ( !reader.IsDBNull(3) )
									m_orig_amt = reader.GetDecimal(3).ToString();
								else	
									m_orig_amt = "";
    						if ( !reader.IsDBNull(4) )
									m_orig_curr = (string)reader["ORIG_CURRENCY"];
								else
									m_orig_curr ="";
							}
						}
						else //Mid not found. Report ADD entry to Diff table/
						{
							Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",srcMid, "Src not found",Pkey,0,0,0,0,0);
							m_Connection2.Execute(Cmd,true);
							//ReportError(string.Format("FTFeedDll: Source Mid - {0} not found. Batch - {1}.",srcMid,Pkey), 1);
							continue;
						}
						foreach (OracleParameter param in command.Parameters)
						{
							param.Dispose();
						}
						reader.Close();
						command.Dispose();
					}
					else		// SQL. this is for testing ONLY. Source is ORA always but who knows.
					{
						if (m_vendor=="FTS")
							Cmd = string.Format("select p_msg_class,p_mid,p_orig_instr_id,p_orig_sttlm_amt,p_orig_sttlm_ccy,p_msg_sts from {0}minf where p_mid='{1}'",m_prefix, srcMid);
						else
			    		Cmd = string.Format("select msg_class,mid,orig_reference,orig_amount,orig_currency,msg_status from {0}mif where mid='{1}'",m_prefix, srcMid); 
						try { m_Connection.OpenDataReader(Cmd); }
						catch (Exception ex)
						{
							ReportError(string.Format("FTFeedDll: SQL error: {0}. Exiting.,",ex.Message),2);
							m_MsgRpt++;
							continue;
						}
						if (m_Connection.SQLDR.Read())
						{
							MsgCls.Add (m_Connection.SQLDR[0].ToString().Trim());
							SrcMids.Add (m_Connection.SQLDR[1].ToString().Trim());
							m_orig_ref = (m_Connection.SQLDR[2].ToString().Trim());
							m_orig_amt = (m_Connection.SQLDR[3].ToString().Trim());
							m_orig_curr = (m_Connection.SQLDR[4].ToString().Trim());
							SrcMsgStatus.Add (m_Connection.SQLDR[5].ToString().Trim());
						}
						else	// No Trg message found
						{
							Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",srcMid, "Src not found",Pkey,0,0,0,0,0);
							m_Connection2.Execute(Cmd,true);
							//ReportError(string.Format("FTFeedDll: Source Mid - {0} not found. Batch - {1}.",srcMid,Pkey), 1 );
							continue;
						}
						m_Connection.CloseDataReader();
					}
					if (m_orig_amt.Length==0)  // in case of bad msg . it should not be here anyway.
						m_orig_amt="0";
		
					if (MsgCls[MidIndex]== "OPI" )	// need to construct the list of SRC MIDS msgs
					{
						MidIndex++;
						if (m_dbtype == "ORA")  // look Src mid for OPI class and get SI class via mfamilty table
						{
							OracleCommand command = m_OraConnection.CreateCommand();
							if (m_vendor=="FTS")
								command.CommandText = string.Format("select a.p_msg_sts,a.p_msg_class, a.p_mid from {0}minf a, {0}mfamily b where a.p_mid=b.related_mid and b.p_mid='{1}'",m_prefix, srcMid);
							else
								command.CommandText = string.Format("select a.msg_status,a.msg_class, a.mid from {0}mif a, {0}mfamily b where a.mid=b.childmid and b.parentmid='{1}'",m_prefix, srcMid);
		
							OracleDataReader reader;
							try { reader = command.ExecuteReader(); }
							catch (Exception ex)
							{
								ReportError(string.Format("FTFeedDll: 2. Oracle error: {0},",ex.Message),1);
								m_MsgRpt++;
								continue;
							}
							if (reader.Read())
							{
								if (m_vendor=="FTS")
								{
									MsgCls.Add ((string)reader["P_MSG_CLASS"]);
									SrcMsgStatus.Add ((string)reader["P_MSG_STS"]);
									SrcMids.Add ((string)reader["P_MID"]);
								}
								else
								{
									MsgCls.Add ((string)reader["MSG_CLASS"]); 
									SrcMsgStatus.Add ((string)reader["MSG_STATUS"]); 
									SrcMids.Add ((string)reader["MID"]); 
								}
								if (Show)
									Console.WriteLine ("----- ORA Inserting " + MsgCls[MidIndex] + " Child mid - " + SrcMids[MidIndex]);
							}
							else //Mid not found. Report ADD entry to Diff table/
							{
								Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",srcMid, "Child not Found",Pkey,0,0,0,0,0);
								m_Connection2.Execute(Cmd,true);
								//ReportError(string.Format("FTFeed: Source ChildMid for OPI {0} not found. Batch - {1}.",srcMid,Pkey), 1);
								continue;
							}
							foreach (OracleParameter param in command.Parameters)
							{
								param.Dispose();
							}
							reader.Close();
						command.Dispose();						}
						else		// SQL. this is for testing ONLY. Source is ORA always but who knows.
						{
							if (m_vendor=="FTS")
								Cmd = string.Format("select a.p_msg_class, a.p_mid, a.p_msg_sts from {0}minf a, {0}mfamily b where a.p_mid=b.related_mid and b.p_mid='{1}'",m_prefix, srcMid);
							else
								Cmd = string.Format("select a.msg_class, a.mid, a.msg_status from {0}mif a, {0}mfamily b where a.mid=b.childmid and b.parentmid='{1}'",m_prefix, srcMid);
							try { m_Connection.OpenDataReader(Cmd); }
							catch (Exception ex)
							{
								ReportError(string.Format("FTFeed: SQL error: {0},",ex.Message),1);
								m_MsgRpt++;
								continue;
							}
							if (m_Connection.SQLDR.Read())
							{
								MsgCls.Add ( m_Connection.SQLDR[0].ToString().Trim() );
								SrcMids.Add (m_Connection.SQLDR[1].ToString().Trim() );
								SrcMsgStatus.Add ( m_Connection.SQLDR[2].ToString().Trim() );
								if (Show)
									Console.WriteLine ("----- SQL Inserting " + MsgCls[MidIndex] + " Child mid - " + SrcMids[MidIndex]);
							}
							else	// No Trg message found
							{
								Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",srcMid, "Child not Found",Pkey,0,0,0,0,0);
								m_Connection2.Execute(Cmd,true);
								//ReportError(string.Format("FTFeed: SQL target ChildMid for OPI {0} not found. Batch - {1}.",srcMid,Pkey), 1 );
								continue;
							}
							m_Connection.CloseDataReader();
						}
					}
		
					//Construct the pairs of mids to compare later on..
					for (int i = 0; i < SrcMids.Count; i++)
					{
						if (Show)
							Console.WriteLine("-------- Src mid Array - " + SrcMids[i] + "  msg class - " + MsgCls[i]);
						if (m_trgdbtype == "ORA")  // look for the corresponding Trg mid
						{
							OracleCommand command1 = m_TrgConnection.CreateCommand();
		
							string lmid = SrcMids[0];  //First MID is the p_orig_instr_id in TRG env.
							//CHANGE THIS: comment or change to test.
							//lmid="1211211192711B00";	// for testing ONLY
		
							if (LookbyRef)
							  if (m_vendor=="FTS")
									command1.CommandText = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_msg_class='{1}'",lmid, MsgCls[i]);
								else
									command1.CommandText = string.Format("select mid,msg_status from mif where orig_reference='{0}' and msg_class='{1}'",lmid, MsgCls[i]); 
							else
								if (m_vendor=="FTS")
									command1.CommandText = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_orig_sttlm_amt='{1}' and p_orig_sttlm_ccy='{2}' and p_msg_class='{3}'",m_orig_ref,m_orig_amt,m_orig_curr, MsgCls[i]);
								else
									command1.CommandText = string.Format("select mid,msg_status from mif where orig_reference='{0}' and orig_amount='{1}' and orig_currency='{2}' and msg_class='{3}'",m_orig_ref,m_orig_amt,m_orig_curr, MsgCls[i]); 
							OracleDataReader reader1;
							try { reader1 = command1.ExecuteReader(); }
							catch (Exception ex)
							{
								ReportError(string.Format("FTFeedDll: 3.Oracle error: {0},",ex.Message),1);
								m_MsgRpt++;
								/* in case of : ORA-00604, ORA-01000
								ORA-00604: error occurred at recursive SQL level 1
								ORA-01000: maximum open cursors exceeded,
								*/
								if ( (ex.Message.IndexOf("ORA-00604") != -1) ||
								     (ex.Message.IndexOf("ORA-01000") != -1) )
								{
									if ( m_dbtype=="ORA" )
									{
										m_OraConnection.Close();
										m_OraConnection.Dispose();
										m_OraConnection=new OracleConnection();
										m_OraConnection.ConnectionString = m_src_connection_string;
										m_OraConnection.Open();
									}
									if ( m_trgdbtype=="ORA" )
									{
										m_TrgConnection.Close();
										m_TrgConnection.Dispose();
										m_TrgConnection=new OracleConnection();
										m_TrgConnection.ConnectionString = m_trg_connection_string;
										m_TrgConnection.Open();
									}
									command1.Dispose();
								}
								continue;
							}
							if (reader1.Read())
							{
								if (m_vendor=="FTS")
								{
									TrgMids.Add ((string)reader1["P_MID"]);
									TrgMsgStatus.Add ((string)reader1["P_MSG_STS"]);
								}
								else
								{
									TrgMids.Add ((string)reader1["MID"]); 
									TrgMsgStatus.Add ((string)reader1["MSG_STATUS"]); 
								}
							}
							else //Mid not found. Try to lookup without msg_class. Sometimes OPI is not opi till later.
							{
								reader1.Close();
								if (LookbyRef)
									if (m_vendor=="FTS")
										command1.CommandText = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}'",lmid);
									else
										command1.CommandText = string.Format("select mid,msg_status from mif where orig_reference='{0}'",lmid); 
								else
									if (m_vendor=="FTS")
										command1.CommandText = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_orig_sttlm_amt='{1}' and p_orig_sttlm_ccy='{2}'",m_orig_ref,m_orig_amt,m_orig_curr);
									else
										command1.CommandText = string.Format("select mid,msg_status from mif where orig_reference='{0}' and orig_amount='{1}' and orig_currency='{2}'",m_orig_ref,m_orig_amt,m_orig_curr); 
								reader1 = command1.ExecuteReader();
								if (reader1.Read() && ( i == 0 ) )
								{
									if (m_vendor=="FTS")
									{
										TrgMids.Add ((string)reader1["P_MID"]);
										TrgMsgStatus.Add ((string)reader1["P_MSG_STS"]);
									}
									else
									{
										TrgMids.Add ((string)reader1["MID"]); 
										TrgMsgStatus.Add ((string)reader1["MSG_STATUS"]); 
									}
								}
								else // Report ADD entry to DiffSummary  table
								{
									if (i > 0)
										Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",SrcMids[i], "SI or Trg Mid Not Found",Pkey,0,0,0,0,0);
									else
										Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",SrcMids[i], "MID Not Found",Pkey,0,0,0,0,0);
									m_Connection2.Execute(Cmd,true);
									//ReportError(string.Format("FTFeedDll: target for Src Mid - {0} Class - {1} not found. Batch - {2}.",SrcMids[i],  MsgCls[i], Pkey), 1 );
									m_MsgRpt++;
									continue;
								}
							}
							foreach (OracleParameter param in command1.Parameters)
							{
								param.Dispose();
							}
							reader1.Close();
							command1.Dispose();
						}
						else
						{
							string lmid = SrcMids[0]; //First MID is the orig_reference in TRG env.
							//CHANGE THIS: comment or change to test.
							//lmid="SCENARIO4";	// for testing ONLY
							if (LookbyRef)
								if (m_vendor=="FTS")
									Cmd = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_msg_class='{1}'",lmid, MsgCls[i]);
								else
									Cmd = string.Format("select mid,msg_status from mif where orig_reference='{0}' and msg_class='{1}'",lmid, MsgCls[i]);
							else
								if (m_vendor=="FTS")
									Cmd = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_orig_sttlm_amt='{1}' and p_orig_sttlm_ccy='{2}' and p_msg_class='{3}'",m_orig_ref,m_orig_amt,m_orig_curr, MsgCls[i]);
								else
									Cmd = string.Format("select mid,msg_status from mif where orig_reference='{0}' and orig_amount='{1}' and orig_currency='{2}' and msg_class='{3}'",m_orig_ref,m_orig_amt,m_orig_curr, MsgCls[i]); 
							try { m_Connection.OpenDataReader(Cmd); }
							catch (Exception ex)
							{
								ReportError(string.Format("FTFeed: SQL error: {0},",ex.Message),1);
								m_MsgRpt++;
								continue;
							}
							if (m_Connection.SQLDR.Read())
							{
								TrgMids.Add (m_Connection.SQLDR[0].ToString().Trim());
								TrgMsgStatus.Add (m_Connection.SQLDR[1].ToString().Trim());
							}
							else	// Try without msg_class
							{
								m_Connection.CloseDataReader();
								if (LookbyRef)
									if (m_vendor=="FTS")
										Cmd = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}'",lmid);
									else
										Cmd = string.Format("select mid,msg_status from mif where orig_reference='{0}'",lmid);
								else
									if (m_vendor=="FTS")
										Cmd = string.Format("select p_mid,p_msg_sts from minf where p_orig_instr_id='{0}' and p_orig_sttlm_amt='{1}' and p_orig_sttlm_ccy='{2}'",m_orig_ref,m_orig_amt,m_orig_curr);
									else
										Cmd = string.Format("select mid,msg_status from mif where orig_reference='{0}' and orig_amount='{1}' and orig_currency='{2}'",m_orig_ref,m_orig_amt,m_orig_curr); 
		
								m_Connection.OpenDataReader(Cmd);
								if (m_Connection.SQLDR.Read() && ( i == 0 ) )
								{
									TrgMids.Add (m_Connection.SQLDR[0].ToString().Trim());
									TrgMsgStatus.Add (m_Connection.SQLDR[1].ToString().Trim());
								}
								else	// No Trg message found
								{
									if (i > 0)
										Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",SrcMids[i], "SI or Trg Mid Not Found",Pkey,0,0,0,0,0);
									else
										Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",SrcMids[i], "MID Not Found",Pkey,0,0,0,0,0);
									m_Connection2.Execute(Cmd,true);
									//ReportError(string.Format("FTFeedDll: target for Src Mid - {0} Class - {1} not found. Batch - {2}.",SrcMids[i], MsgCls[i], Pkey), 1 );
									m_MsgRpt++;
									continue;
								}
							}
							m_Connection.CloseDataReader();
						}
					}
					//We have to make sure that # of srcs = to # of trgs
					int noofmids = SrcMids.Count;
					if (noofmids > TrgMids.Count)
					{
						noofmids=TrgMids.Count; // it reports SI which are missing. They are reported above. Leave here just in case
						//Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} )",SrcMids[noofmids], "SNI not found",Pkey,0,0,0,0,0);
						//m_Connection2.Execute(Cmd,true);
						//ReportError(string.Format("FTFeedDll: SI target Mid for OPI {0} not found. Batch - {1}.",SrcMids[noofmids],Pkey), 1 );
					}
					for (int i = 0; i < noofmids; i++)
					{
						m_MsgRpt++;
						if (m_MsgRpt % 100 == 0)		// update each 100 msgs
						{
							UpdateBatchTables (m_MsgRpt,"Compare Running", "D");
						}
						if (Show)
						{
							Console.WriteLine ("Src number - " + SrcMids.Count + " Trg count - " + TrgMids.Count + "  Cls count - " + MsgCls.Count);
							Console.WriteLine ("----  DO compare for Src - " + SrcMids[i] + " Trg mid - " + TrgMids[i] + " - class- " + MsgCls[i]);
						}
		
						if (TrgMsgStatus[i] == "AGED")
							TrgMsgStatus[i] = "COMPLETE";
						if (SrcMsgStatus[i] == "AGED")
							SrcMsgStatus[i] = "COMPLETE";
		
						if (TrgMsgStatus[i] != SrcMsgStatus[i]) // check statuses if they are compatible then do compare.
						{
							Cmd = string.Format ("insert into DiffSummary values ('{0}','{1}','{2}', {3},{4},{5},{6},{7} );",SrcMids[i],TrgMids[i],Pkey,1,1,0,0,0);
							Cmd = Cmd + string.Format("insert into DiffResults values ({0},'{1}','{2}','{3}','{4}','{5}','{6}');", Pkey, SrcMids[i], TrgMids[i], "msg_status", SrcMsgStatus[i], TrgMsgStatus[i], "mif");
							m_Connection2.Execute(Cmd,true);
						}
						else
							NoDiff = compit.Compare(Show, Pkey, m_table, m_Area, SrcMids[i], TrgMids[i], m_src_connection_string, m_trg_connection_string, m_dbtype, m_trgdbtype, m_prefix );
						if( NoDiff < 0 )	// indiction
						{
							Console.WriteLine ("bad compare");
						}
					}
				}
				else   break;
			} while (true);
		
			try {
				m_OraConnection.Close();
				m_OraConnection.Dispose();
				m_TrgConnection.Close();
				m_TrgConnection.Dispose();
			}
			catch {};
		
			sRdr.Close();
		
			UpdateBatchTables (m_MsgRpt,"Compare Finished", "D");
			Cmd = string.Format("update BatchQ set ProcessState = 'P'," +
			" LoadTime = '{0}' where " +
			"Pk = {1}",  DateTime.Now, Pkey);
			m_Connection2.Execute(Cmd,true);
		
			if (Show)
				Console.WriteLine ("batch - " + Pkey + " has ended successfully");
			return;
		}

		private void ReportError(string ErrMsg, int Arg)
		{
			if (Show)
				Console.WriteLine(ErrMsg);
			_simLog.Source = "FtFeeder";
		
			// Write an error entry to the event log.
			if (Arg == 2)
			{
				_simLog.WriteEntry(ErrMsg, EventLogEntryType.Error);
				string Cmd = string.Format("update BatchDescr set BatchStatus = 'Aborted. Check Logs.' where BatchId = '{0}'", Pkey);
				m_Connection2.Execute(Cmd,true);
			}
			SimLog.log.write(m_Area, ErrMsg, true);
			return;
		}

		private void UpdateBatchTables (long Tot, string status, string what)
		{
			string Cmd="";
			if (what == "R")	//Replay
				Cmd = string.Format ("update BatchDescr set BatchStatus = '{2}', NoReplay = {1} where BatchId = '{0}'",Pkey,Tot,status);
			else			//Compare
				Cmd = string.Format ("update BatchDescr set TimeEnd='{3}', BatchStatus = '{2}', NoDiffs = {1} where BatchId = '{0}'",Pkey,Tot,status, DateTime.Now);
			m_Connection2.Execute(Cmd,true);
		}

		private bool GetMsgsFromORA (int i)
		{
			m_OraConnection=new OracleConnection();
			m_OraConnection.ConnectionString = m_src_connection_string;
			m_OraConnection.Open();
			if (Show)
			{
				Console.WriteLine("State: {0}", m_OraConnection.State);
				Console.WriteLine("ConnectionString: {0}", m_OraConnection.ConnectionString);
			}
			/*
			Msg_class in FT
			#define MSG_CLASS_PAY              "PAY"         // Regular Payment
			#define MSG_CLASS_PI               "PI"          // Payment Instruction
			#define MSG_CLASS_ROF              "ROF"         // Return of Funds Payment
			#define MSG_CLASS_OPI              "OPI"         // PI outgoing
			#define MSG_CLASS_SN               "SN"          // Settlement Notification
			#define MSG_CLASS_SI               "SI"          // Settelement Instruction
			#define MSG_CLASS_AF               "AF"          // Anticipated fund
			#define MSG_CLASS_DD               "DD"          // Direct Debit
			#define MSG_CLASS_NAC              "NAC"         // Non-accounting Message
			#define MSG_CLASS_CHG              "CHG"         // Request for charges msg (MT191-->MT202)
		
			services in MIF
			CSW
			TLX
			DEX
			CDI
			FRB
			CDT
			CPS
			SWF
			*/
		
			OracleCommand command = m_OraConnection.CreateCommand();
			if (Show)
				Console.WriteLine("sql - {0}", m_Sql[i] );
		
			command.CommandText = (string)m_Sql[i];
			OracleDataReader reader;
			try { reader = command.ExecuteReader(); }
			catch (Exception ex)
			{
				ReportError(string.Format("FTFeed: Oracle error: {0},",ex.Message),2);
				return (false);
			}
		
			int Cnt=0;
			long Tot=0;
			while (reader.Read())
			{
				if (Cnt > 99)
				{
					string Cmd = string.Format("update BatchDescr set NoReplay = {1} where BatchId = '{0}'", Pkey, Tot);
					m_Connection2.Execute(Cmd,true);
					Cnt=0;
				}
				if (Tot == 1)  // once per batch
				{
					string Cmd = string.Format ("update BatchDescr set BatchStatus = 'Replay Started' where BatchId = '{0}'",Pkey);
					m_Connection2.Execute(Cmd,true);
				}
				string src = (string)reader["SERVICE"];
				string mid = (string)reader["MID"];
				string txt = (string)reader["CONTENTS"];
				string office = (string)reader["OFFICE"];
				DateTime rcvtime = (DateTime)reader["CREATE_DATE"];
				if ( !PutMessage(src,mid,txt,office,rcvtime.ToString() ) )
				{
					ReportError (string.Format("Mid- {0} skipped.", mid),1);
				}
				else
				{
					Cnt++;
					Tot++;
				}
			}
			UpdateBatchTables (Tot,"Replay Finished", "R");
			reader.Close();
			if (Show)
				Console.WriteLine("Replayed messages from DB. Total - " + Tot);
			return(true);
		}

		private bool GetMsgsFromSQL (int i)
		{
			string Cmd=(string)m_Sql[i];
		
			try { m_Connection1.OpenDataReader(Cmd); }
			catch (Exception ex)
			{
				ReportError(string.Format("FTFeed: SQL error: {0},",ex.Message),2);
				return(false);
			}
		
			int Cnt=0;
			long Tot=0;
		
			while(m_Connection1.SQLDR.Read())
			{
				if (Cnt > 99)
				{
					Cmd = string.Format("update BatchDescr set NoReplay = {1} where BatchId = '{0}'", Pkey, Tot);
					m_Connection2.Execute(Cmd,true);
					Cnt=0;
				}
				if (Tot == 1)  // once per batch
				{
					Cmd = string.Format ("update BatchDescr set BatchStatus = 'Replay Started' where BatchId = '{0}'",Pkey);
					m_Connection2.Execute(Cmd,true);
				}
				string src = m_Connection1.SQLDR[0].ToString().Trim();
				string mid = m_Connection1.SQLDR[1].ToString().Trim();
				string txt = m_Connection1.SQLDR[2].ToString().Trim();
				string office = m_Connection1.SQLDR[3].ToString().Trim();
				DateTime rcvtime = m_Connection1.SQLDR.GetDateTime(4);
				if ( !PutMessage(src,mid,txt,office,rcvtime.ToString() ) )
				{
					ReportError(string.Format("FTFeeder: Mid- {0} skipped.", mid),1);
				}
				else
				{
					Cnt++;
					Tot++;
				}
			}
			m_Connection1.CloseDataReader();
			UpdateBatchTables (Tot,"Replay Finished", "R");
			return(true);
		}

		private bool PutMessage(string Src,string Mid, string Txt, string office, string rcvTime)
		{
			bool stat=true;
			if (Show)
				Console.WriteLine("Starting to process msgs. Mid - {0}, Service = {1}, Office = {2}, Txt = {3}", Mid, Src, office, Txt);
		
			if (m_sepa=="Y")  // for SEPA only output the mid!!
			{
				sWrt.Write(Mid.ToString() + "\n");
				return true;
			}
		
			string Orig_txt = "";
      switch (m_client.ToLower())
      {
        case "citi":	//
					Orig_txt = FindOrgTextCiti(Txt, Mid);
	        break;
	      case "ftpoc":	//
					Orig_txt = FindOrgTextFtpoc(Txt, Mid);
	        break;
		    default:
				break;
			}
			
			if (Orig_txt.Length < 1)
			return false;
		
			string Line;
		
			Src=Src.Substring(0,3).TrimEnd();
		
			try
			{
				Line = m_sources[Src].ToString();
				if (office.Length > 0)
				{
					Line = office + Line.Substring(3);
				}
			}
			catch
			{
				ReportError(string.Format("No Line for Source - {0}",Src),1);
				//Console.WriteLine ("No Line for Source - {0}",Src);
				return false;
			}
		
			// Watch out for single quotes...
			//  done in loadstringbuf below		Orig_txt = Orig_txt.Replace("'", "''");
		
			if (ChangeRef)
			{
				LookbyRef = true;
				int pos = 0;
				if (Src == "GBS")
				{
					pos = Orig_txt.IndexOf("<REF16.SENDREF>");   //CHips reference this is a mandatory field and has to be here. Same as in SWF
					if (pos != -1)
					{
						pos = pos + 15;
						string s1 = Orig_txt.Substring(0, pos);
						int pos1 = Orig_txt.IndexOf("</REF16.SENDREF>", pos);  // include crlf
						string s2 = Orig_txt.Substring(pos1);
						Orig_txt = s1 + Mid + s2;
					}
				}
				if ((Src == "CPS") || (Src == "CHI"))
				{
					pos = Orig_txt.IndexOf("[320]");   //CHips reference this is a mandatory field and has to be here. Same as in SWF
					if (pos != -1)
					{
						pos = pos + 5;
						string s1 = Orig_txt.Substring(0, pos);
						int pos1 = Orig_txt.IndexOf("*", pos);  // include crlf
						string s2 = Orig_txt.Substring(pos1);
						Orig_txt = s1 + Mid + s2;
					}
				}
				if ((Src == "FRB") || (Src == "FDW"))
				{
					pos = Orig_txt.IndexOf("{3320}");   //FED reference . This one is optional. If not found insert it.
					if (pos != -1)
					{
						pos = pos + 6;
						string s1 = Orig_txt.Substring(0, pos);
						int pos1 = Orig_txt.IndexOf("*", pos);  // include crlf
						string s2 = Orig_txt.Substring(pos1);
						Orig_txt = s1 + Mid + s2;
					}
					else                // insert it.
					{
						pos = Orig_txt.IndexOf("{3400}");
						if (pos != -1)
						{
							string s1 = Orig_txt.Substring(0, pos);
							string s2 = Orig_txt.Substring(pos);
							Orig_txt = s1 + "{3320}" + Mid + "*" + s2;
						}
					}
				}
				// .. and replace F20 with original MID  THIS CODE FOR SWIFT AND SWIFT LIKE.
				if ( (Src  != "FRB") && (Src != "CPS") && (Src  != "FDW") && (Src != "CHI") && (Src != "GBS") )
				{
					pos = Orig_txt.IndexOf(":20:");
					if (pos != -1)
					{
						pos = pos+4;
						string s1 = Orig_txt.Substring(0, pos);
						int pos1 = Orig_txt.IndexOf(":", pos) - 2;  // include crlf
						string s2 = Orig_txt.Substring(pos1);
						Orig_txt = s1 + Mid + s2;
					}
					if ((Src == "CDI") || (Src == "CDT") ) // CitiDirect. Change :POR: with new reference
					{
						pos = Orig_txt.IndexOf(":POR:");
						if (pos != -1)
						{
							pos = pos+5;
							string s1 = Orig_txt.Substring(0, pos);
							int pos1 = Orig_txt.IndexOf(":", pos) - 2;  // include crlf
							string s2 = Orig_txt.Substring(pos1);
							Orig_txt = s1 + Mid + s2;
						}
					}
				}
			}
			else
				LookbyRef =false;
		
		// build header string  and append the orig text...
		
			string header = string.Format("<start rcv_time:{0} line:{1} trn:{2}/{3}>", rcvTime, Line,
			Mid.Substring(0,8), Mid.Substring(8,8));
		
			StringBuilder thisMsg = new StringBuilder();
			thisMsg.Length = 0;
			thisMsg.Append(header);
			thisMsg.Append("\r\n");
			thisMsg.Append(Orig_txt);
			thisMsg.Append("\r\n<end>\r\n");
		
			ldr.LoadStringBuf(m_Area, thisMsg, m_AmtDelta.ToString(), m_timeDelta, m_Alg);
		
			if (Show)
				Console.WriteLine ("Inserting. Line - {0}, Txt - {1}",Line,Orig_txt);
		
			//and output to the file
			sWrt.Write(Mid.ToString() + "\n");
		
			return stat;
		}
	
		private string FindOrgTextFtpoc(string Txt, string Mid)
		{
//We'll start from :Message>
			int pos=0, pos1=0, pos2=0, len=0;
			string Tag="";
			string EndTag="";
			string LookFor = ":Message>";
			pos1=Txt.IndexOf(LookFor);
			if (pos1 > -1) // look for '<'
			{
				for (int i = pos1; i > 0; --i)
				{
					if (Txt[i] == '<')
					{
						pos2=i;
						break;
					}
					len++;
				}
				Tag = Txt.Substring(pos2,len+LookFor.Length);
				EndTag = Tag.Replace("<","</");
			}
			else
			{
				return "";
			}

	//Console.WriteLine ("Find something tag "  + Tag + "  End - "  + EndTag);

			pos=0; pos1=0; pos2=0; len=0;
			pos1=Txt.IndexOf(Tag);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
				pos1+=Tag.Length;
				pos2=Txt.IndexOf(EndTag);
				if (pos2 > pos1)	// make sure that the end tag is further than starting tag
				{
					if(pos2 > -1) // found the end of 1st pair
					{
						pos = pos1;
						len = pos2 - pos1;
					}
					else
						return "";	// failed the parse
				}
			}

			if (len > 0)
			{
				string Org_txt = Txt.Substring(pos, len);
				if (Org_txt.Substring(0,2) == "\r\n" )
					Org_txt = Org_txt.Substring(2,len - 2);
				if (Show)
					Console.WriteLine ("orig - {0}", Org_txt);
		//Console.WriteLine ("Find something "  + Org_txt);
				return Org_txt;
			}
			return Txt.TrimStart();
		}

		private string FindOrgTextCiti(string Txt, string Mid)
		{
/*

This should be adjusted for SP version


First. look for message that is in between  
'----Original Tagged Message Start----' & '----Original Tagged Message End----'  service GBS
'----Original Tagged Citidirect Message Start----' & '----Original Tagged Citidirect Message End----' service CDI, CDT
'----Original Message    ----' & '----Received from Payfix----'
'----Original message start----' & '----Original message end----'
'(CLOB)' & end of it trim trailing spaces

*/

			int pos=0, pos1=0, pos2=0, len=0;
			
			string tag_pair10 = "----Original Tagged Message Start----";
			string end_tag_pair10 = "----Original Tagged Message End----";
			string tag_pair1 = "----Original Tagged Citidirect Message Start----";
			string end_tag_pair1 = "----Original Tagged Citidirect Message End----";
			string tag_pair2 = "----Original message start----";
			string end_tag_pair2 = "----Original message end----";
			string tag_pair3 = "----Original Message    ----";
			string end_tag_pair3 = "----Received from Payfix----";
			string tag_pair4 = "----Received from Payfix----";
			string end_tag_pair4 = "----Original Message    ----";
			string tag_pair5 = "----Original Message    ----";
			string tag_pair6 = "-----";
		//Console.WriteLine ("--------  Starting " + Mid);

			pos1=Txt.IndexOf(tag_pair10);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find 10 " + tag_pair10);
				pos1+=tag_pair10.Length;
				pos2=Txt.IndexOf(end_tag_pair10);
				if (pos2 > pos1)	// make sure that the end tag is further than starting tag
				{
					if(pos2 > -1) // found the end of 1st pair
					{
						pos = pos1;
						len = pos2 - pos1;
		//Console.WriteLine ("Find 10End " + end_tag_pair10);
					}
					else
						return "";	// failed the parse
				}
			}

			pos1=Txt.IndexOf(tag_pair1);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find 1 " + tag_pair1);
				pos1+=tag_pair1.Length;
				pos2=Txt.IndexOf(end_tag_pair1);
				if (pos2 > pos1)
				{
					if(pos2 > -1) // found the end of 1st pair
					{
						pos = pos1;
						len = pos2 - pos1;
		//Console.WriteLine ("Find 1End " + end_tag_pair1);
					}
					else
						return "";	// failed the parse
				}
			}

			pos1=Txt.IndexOf(tag_pair2);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find 2 " + tag_pair2);
				pos1+=tag_pair2.Length;
				pos2=Txt.IndexOf(end_tag_pair2);
				if (pos2 > pos1 )
				{
					if(pos2 > -1) // found the end of 1st pair
					{
						pos = pos1;
						len = pos2 - pos1;
		//Console.WriteLine ("Find 2End " + end_tag_pair2);
					}
					else
						return "";	// failed the parse
				}
			}

			pos1=Txt.IndexOf(tag_pair3);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find 3 " + tag_pair3);
				pos1+=tag_pair3.Length;
				pos2=Txt.IndexOf(end_tag_pair3);
				if (pos2 > pos1)
				{
					if (pos2 > -1) // found the end of 1st pair
					{
						pos = pos1;
						len = pos2 - pos1;
		//Console.WriteLine ("Find 3End " + end_tag_pair3);
					}
					else
						return "";	// failed the parse
				}
			}
	
			pos1=Txt.IndexOf(tag_pair4);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find 4 " + tag_pair4);
				pos1+=tag_pair4.Length;
				pos2=Txt.IndexOf(end_tag_pair4);
				if (pos2 > pos1)
				{
					if(pos2 > -1) // found the end of 1st pair
					{
						pos = pos2 + end_tag_pair4.Length;
						len = Txt.Length - pos;
		//Console.WriteLine ("Find 4End " + end_tag_pair4);
					}
					else
						return "";	// failed the parse
				}
			}
	
			pos1=Txt.IndexOf(tag_pair5);  // RCX for TI source. The orig text is at the end. So, pick it up.
			if ( (pos1 > -1) && (len == 0) ) // found begin of the pair
			{
				pos = pos1 + tag_pair5.Length;
				len = Txt.Length - pos;
			}
	
			pos1=Txt.IndexOf(tag_pair6);
			if ( (pos1 > -1) && (len == 0) ) // found begin of 1st pair
			{
		//Console.WriteLine ("Find nothing " );
				ReportError (string.Format("Unexpected TEXT. txt - {0}, mid - {1}", Txt, Mid),1);
				return "";
			}

			if (len > 0)
			{
				string Org_txt = Txt.Substring(pos, len);
				if (Org_txt.Substring(0,2) == "\r\n" )
					Org_txt = Org_txt.Substring(2,len - 2);
				if (Show)
					Console.WriteLine ("orig - {0}", Org_txt);
		//Console.WriteLine ("Find something "  + Org_txt);
				return Org_txt;
			}
		//Console.WriteLine ("Full text "  + Txt.TrimStart());
			return Txt.TrimStart();
		}

#region commonstuff
		private void GetLineContext()
		{

	    		string Cmd = string.Format("select count(*) from MQLINESRC");
			m_Connection.OpenDataReader(Cmd);
			m_Connection.SQLDR.Read();
			int colNdx = m_Connection.SQLDR.GetInt32(0);
			m_Connection.CloseDataReader();
			m_sources=new Hashtable(colNdx);
			Cmd = string.Format("select Source,ExternalSrc, LineName from MQLINESRC");
			m_Connection.OpenDataReader(Cmd);
			while(m_Connection.SQLDR.Read())
			{
				string Src=m_Connection.SQLDR[0].ToString().Trim();
				string ExtSrc=m_Connection.SQLDR[1].ToString().Trim();
				string Lname=m_Connection.SQLDR[2].ToString().Trim();
				m_sources.Add(ExtSrc,Lname);
				if (Show)
					Console.WriteLine ("service - {0}, line - {1}", ExtSrc, Lname);
			}
			m_Connection.CloseDataReader();
			if (m_cref == "")
			{
				Cmd = string.Format("select ChangeRef from SimulatorControl");
				m_Connection.OpenDataReader(Cmd);
				m_Connection.SQLDR.Read();
				m_cref = m_Connection.SQLDR[0].ToString();
				m_Connection.CloseDataReader();
				if (m_cref == "Y")
				{
					ChangeRef = true;
					LookbyRef = true;
				}
				else		//Default is Change the Reference
				{
					ChangeRef = false;
					LookbyRef = false;
				}			
			}
			if (Show)
			{
				foreach (string key in m_sources.Keys)
				{
				    Console.WriteLine("key - {0}, value - {1}",key, m_sources[key]);
				}
			}
		}
#endregion
	}
}
