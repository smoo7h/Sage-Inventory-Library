using DevExpress.ExpressApp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.Xpo;

using DevExpress.Data.Filtering;

namespace SageImportApp
{
    public class SageHelper
    {
        public static IList<WeeklyInventory> GetThisWeeksInventroy(DateTime StartDate, DateTime EndDate, IObjectSpace space)
        {
            IList<WeeklyInventory> weeklyInventory = new List<WeeklyInventory>();

           // IObjectSpace space = AppHelper.Application.CreateObjectSpace();
            //get the list of locations
            IList<Location> locationList = space.GetObjects<Location>();
            bool groupCheck = false;
            foreach (Location loc in locationList)
            {

          
                //filter the measures by LDC and date
                if (loc.Name.Contains("IPS"))
                {
                    Console.Write('s');
                }
                WeeklyInventory WeekI = new WeeklyInventory();
                WeekI.Location = loc;
               
                XPCollection<Move> moves = loc.MoveFrom;

                // BetweenOperator betOp = new BetweenOperator("MoveDate", DateTime.Now.AddDays(-97), DateTime.Now.AddDays(-90));

                BetweenOperator betOp = new BetweenOperator("MoveDate", StartDate, EndDate);
                NotOperator notOP = new NotOperator(new NullOperator("Appliance"));

                GroupOperator grpOP = new GroupOperator();
                grpOP.OperatorType = GroupOperatorType.And;
                grpOP.Operands.Add(betOp);
                grpOP.Operands.Add(notOP);

                moves.Filter = grpOP;
         //       moves.Filter = betOp;

                

                List<InventoryItem> invList = new List<InventoryItem>();

                //loc.MoveFrom[0].Appliance




                foreach (Move mov in moves)
                {
                    FastFileLineItem item = space.FindObject<FastFileLineItem>(new BinaryOperator("Move.Oid", mov.Oid.ToString()));

                    foreach (InventoryItem inv in invList)
                    {
                        if (item.HAPApplication != null && mov.Appliance.SageName == inv.Appliance.SageName && mov.LocationTo.Name == inv.LocationTo.Name 
                            && item.HAPApplication.BasicScheduledDate.ToString("dd/MM/yyyy") == inv.InstallDate.ToString("dd/MM/yyyy"))
                        {
                            inv.Quantity += mov.Quantity;
                            groupCheck = true;
                        }
                        else if (item.ENBLIApplication != null && mov.Appliance.SageName == inv.Appliance.SageName && mov.LocationTo.Name == inv.LocationTo.Name
&& item.ENBLIApplication.TAPSAssessmentDate.ToString("dd/MM/yyyy") == inv.InstallDate.ToString("dd/MM/yyyy"))
                        {
                            inv.Quantity += mov.Quantity;
                            groupCheck = true;
                        }
                        else if (item.ENBLIApplication != null && mov.Appliance.SageName == inv.Appliance.SageName && mov.LocationTo.Name == inv.LocationTo.Name
                           && item.ENBLIApplication.ENBLIADate.ToString("dd/MM/yyyy") == inv.InstallDate.ToString("dd/MM/yyyy"))
                        {
                            inv.Quantity += mov.Quantity;
                            groupCheck = true;
                        }

                        
                    }

                    if (!groupCheck)
                    {
                        if (item.HAPApplication!=null)
                        {

                            InventoryItem invItem = new InventoryItem();
                            invItem.Appliance = mov.Appliance;
                            invItem.LocationTo = mov.LocationTo;
                            invItem.Quantity = mov.Quantity;
                            invItem.InstallDate = item.HAPApplication.BasicScheduledDate;
                            invList.Add(invItem);
                        }
                        else if (item.ENBLIApplication != null)
                        {
                             InventoryItem invItem = new InventoryItem();
                            invItem.Appliance = mov.Appliance;
                            invItem.LocationTo = mov.LocationTo;
                            invItem.Quantity = mov.Quantity;

                            if (item.ENBLIApplication.TAPSAssessmentDate != null && item.ENBLIApplication.TAPSAssessmentDate != DateTime.MinValue)
                            {
                                invItem.InstallDate = item.ENBLIApplication.TAPSAssessmentDate;
                                
                            }
                            else
                            {
                                invItem.InstallDate = item.ENBLIApplication.ENBLIADate;
                            }

                            
                            invList.Add(invItem);
                        }
                    }

                    groupCheck = false;

                }

                if (invList.Count > 0)
                {
                    WeekI.InventoryItemList = invList;
                    weeklyInventory.Add(WeekI);              
                }

            }

            return weeklyInventory;
        }

      
        public static void SageInvUpdate(string prodName, Int32 qtyMoved, string loc, DateTime installDate)
        {

            double currSageQty;
            double newSageQty;
            double unitCost;
            double sageInvValue;
            double changeInvValuNeg;
            double changeInvValuPos;
            double curInvBalance;
            double curHAPBalance;
            int negQtyMoved = qtyMoved * -1;
            string sPartCode;
            int locId;
            Int32 inventId;
            Int32 primKey;
            Int32 nextPrimKey;
            Int32 journIdforTitrec;
            OdbcDataReader DbReader = null;
            OdbcCommand DbCommand = new OdbcCommand();

            //DbConnection for dbConReader
            string connectionString = "";

            if (ConfigurationManager.ConnectionStrings["SageConnectionString"] != null)
            {
                connectionString = ConfigurationManager.ConnectionStrings["SageConnectionString"].ConnectionString;            
            }

            OdbcConnection DbConnection = new OdbcConnection(connectionString);
            DbConnection.Open();

            //Time stuff below was attempting to EXCLUDE the date portion when doing the CURTIME() sql function.

            //grabs sPartCode and Id from tinvent
            string sqlPartCode = "SELECT sPartCode, lId FROM tinvent WHERE sName LIKE '" + prodName + "';";

            //This will grab the current qty according to the prodName in SAGE database, a few values are grabbed and set/used in first DbReader.Read();
            string sqlSageQty = "SELECT dInStock, dLastCost FROM tinvbyln LEFT JOIN tinvent ON tinvbyln.lInventId = tinvent.lId WHERE tinvent.sName LIKE '" + prodName + "';";


            //Grab location ID 
            string sqlLocId = "SELECT lId FROM tinvloc WHERE sGrpCode LIKE '" + loc + "';";
            DbReader = dbConReader(sqlSageQty, DbConnection, DbReader, DbCommand);

            //Grabs as string and converts to double for updates and inserts
            string sageQty = DbReader.GetString(0);
            string uCost = DbReader.GetString(1);

            currSageQty = Convert.ToDouble(sageQty);
            unitCost = Convert.ToDouble(uCost);

            //New sage QTY to input
            newSageQty = currSageQty - qtyMoved;

            //New total sage Inventory value
            sageInvValue = newSageQty * unitCost;

            //for Insert into titrec make value negative because all changes are moves ie decrease in Inventory Assets
            changeInvValuNeg = (unitCost * qtyMoved) * -1;
            changeInvValuPos = (unitCost * qtyMoved);

            //Set partCode and inventory ID values for SQL inserts/Updates
            DbReader = dbConReader(sqlPartCode, DbConnection, DbReader, DbCommand);

            sPartCode = DbReader.GetFieldValue<string>(0);
            inventId = DbReader.GetFieldValue<Int32>(1);

            //Grab Location ID according to Text file.
            DbReader = dbConReader(sqlLocId, DbConnection, DbReader, DbCommand);

            locId = DbReader.GetFieldValue<int>(0);

            //Updates tinvbyln with the new values as per parameters of sageInvUpdate
            string updateTinvbyln = "UPDATE tinvbyln SET lInvLocId=" + locId + ", dtASDate=CURDATE(), tmASTime=CURTIME(), dInStock = '" + newSageQty + "', dCostStk='" + sageInvValue
                + "' WHERE tinvbyln.lInventId LIKE (SELECT tinvent.lId FROM tinvent WHERE sName LIKE '" + prodName + "');";

            //update the tinvent table with new times according to prodName
            string updateTinvent = "UPDATE tinvent SET dtASDate = CURDATE(), tmASTime=CURTIME() WHERE sName LIKE '" + prodName + "';";

            //grabs next primKey for titrec/tjentact
            string tjenPrimKey = "SELECT lNextId FROM tnxtpids WHERE lId LIKE 100";

            string titPrimKey = "SELECT lNextId FROM tnxtpids WHERE lId LIKE 150;";

            //Update tinvbyln with changes
            DbReader = dbConReader(updateTinvbyln, DbConnection, DbReader, DbCommand);

            //Update tinvent with changes
            DbReader = dbConReader(updateTinvent, DbConnection, DbReader, DbCommand);


            //Grabbing primKeys for tjen/tjourent
            DbReader = dbConReader(tjenPrimKey, DbConnection, DbReader, DbCommand);

            primKey = DbReader.GetFieldValue<Int32>(0);

            nextPrimKey = DbReader.GetFieldValue<Int32>(0) + 1;

            //Update the tijentact primary key in tnxtpids
            string updateTjentactPrimKey = "UPDATE tnxtpids SET dtASDate=CURDATE(), tmASTime=CURTIME(),"
                + "sASOrgId='winsim', lNextId=" + nextPrimKey + " WHERE lId LIKE 100;";
            //Updating next prime keys for tnxtpids
            DbReader = dbConReader(updateTjentactPrimKey, DbConnection, DbReader, DbCommand);
            

            //Insert new record into tjourent table with next primKey
            string insertTjourent = "INSERT INTO tjourent (lId, dtASDate, tmASTime, sASOrgId, dtJourDate, nModule, nType,lCurrncyId, dExchRate, lRecId, nPymtClass,"
                + "bExported, lCompId, bAcctEntry, bAEImport, bAftYEnd, bB4YrStart) VALUES (" + primKey + ", " + installDate.ToString("dd/MM/yyyy") + ", " + installDate.ToLocalTime().ToString() + ", 'winsim', " + installDate.ToString("dd/MM/yyyy") + ", 4, 1, 1, 0, -1, 0 ,0, 1, 0,0,0,0);";
            //Fill the nLine numbers. 15150000 Acct is inventory account, 51470000 is HAP account (things going out of inv go into HAP)
            string insertTjentactOne = "INSERT INTO tjentact (lJEntId, nLineNum, lAcctID, dAmount, dAmountFor, lAcctDptId, lCompId) VALUES ("
                + primKey + ", 1, 51470000, " + changeInvValuPos + ", 0, 0, 1);";

            string insertTjentactTwo = "INSERT INTO tjentact (lJEntId, nLineNum, lAcctID, dAmount, dAmountFor, lAcctDptId, lCompId) VALUES ("
                + primKey + ", 2, 15150000, " + changeInvValuNeg + ", 0, 0, 1);";
            //Creating new records for tjourent and both lines of tjentact
            DbReader = dbConReader(insertTjourent, DbConnection, DbReader, DbCommand);


            //nline 1 insert
            DbReader = dbConReader(insertTjentactOne, DbConnection, DbReader, DbCommand);

            //nline 2 insert
            DbReader = dbConReader(insertTjentactTwo, DbConnection, DbReader, DbCommand);

            //Grabs primKey for titrec/titluli/titrline
            DbReader = dbConReader(titPrimKey, DbConnection, DbReader, DbCommand);

            primKey = DbReader.GetFieldValue<Int32>(0);

            nextPrimKey = DbReader.GetFieldValue<Int32>(0) + 1;

            //update the titrec prim key in tnxtpids
            string updateTitrecPrimKey = "UPDATE tnxtpids SET dtASDate=CURDATE(), tmASTime= CURTIME(),"
                + "sASOrgId='winsim', lNextId=" + nextPrimKey + " WHERE tnxtpids.lId LIKE '150';";

            //Updating titrec/Titluli/titrline primekey in tnxtpids
            DbReader = dbConReader(updateTitrecPrimKey, DbConnection, DbReader, DbCommand);


            //Journal ID for titrec entry.
            string sqlJourId = "SELECT lId FROM tjourent ORDER BY lId DESC LIMIT 1;";
            DbReader = dbConReader(sqlJourId, DbConnection, DbReader, DbCommand);
            journIdforTitrec = DbReader.GetFieldValue<Int32>(0);

            //Insert new record into titrec table with next primKey
            string insertTitrec = "INSERT INTO titrec (lId,dtASDate, tmASTime, sASOrgId, lVenCusId, lJourId, nTsfIn, dtJournal,dtUsing, dFreight,dInvAmt,fDiscPer,nDiscDay,nNetDay,dDiscAmt,bCashTrans,bCashAccnt,b40Data,"
            + "bReversal,bReversed,bFromPO,bPdByCash,bPdbyCC,bDiscBfTax,bFromImp,bUseMCurr,bLUCleared,bStoreDuty,lCurIdUsed,dExchRate,bPrinted,bEmailed,lChqId,bChallan,bPaidByWeb,nOrdType,bPrePaid,lOrigPPId,lPPId,dPrePAmt,lSoldBy,"
            + "bDSProc,lInvLocId,bTrfLoc,bRmBPLst,bPSPrintd,bPSRmBPLst,lCCTransId,bPdByDP) VALUES(" + primKey + ", " + installDate.ToString("dd/MM/yyyy") + ",CURTIME(), 'winsim',0," + journIdforTitrec + ",0," + installDate.ToString("dd/MM/yyyy") + "," + installDate.ToString("dd/MM/yyyy") + ",0,"
                + changeInvValuNeg + ",0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0," + locId + ",0,0,0,0,0,0);";

            //Insert new titrec record
            DbReader = dbConReader(insertTitrec, DbConnection, DbReader, DbCommand);

            //Insert new record into titluli uses titrec prime key
            string insertTitluli = "INSERT INTO titluli (lITRecId, nLineNum, sItem, sUnits, nUnitType, sDesc, dAmtInclTx, dOrdered, dRemaining, dPrice, dDutyPer, dDutyAmt, bFreight,"
                + "lTaxCode, lTaxRev, dTaxAmt, bTSLine, dBasePrice, dLineDisc, dVenRel, bVenToStk, bDefDesc) VALUES (" + primKey + ", 1,'" + sPartCode + "', 'Each',"
                + "1, '" + prodName + "'," + changeInvValuNeg + ",0,0," + unitCost + ",0,0,0,0,0,0,0,0,0,0,1,1);";

            //insert new titluli rec
            DbReader = dbConReader(insertTitluli, DbConnection, DbReader, DbCommand);

            //Insert new record into titrline uses tirec prime key
            string insertTitrline = "INSERT INTO titrline (lITRecId, nLineNum, sSource, lInventId, lAcctId, dQty, dPrice, dAmt, dCost, dRev,bTsfIn, bVarLine, bReversal, bService,"
                + "lAcctDptId,lInvLocId, bDefPrc,lPrcListId,dBasePrice,dLineDisc,bDefBsPric,bDelInv,bUseVenItm) VALUES ("
                + primKey + ", 1, '" + sPartCode + "'," + inventId + ", 51470000, " + negQtyMoved + ", " + unitCost + ", " + changeInvValuNeg + "," + changeInvValuNeg + ",0,0,0,0,0,0," + locId + ",0,0,0,0,0,0,0);";

            //insert new titrline rec
            DbReader = dbConReader(insertTitrline, DbConnection, DbReader, DbCommand);

            //Grab current dYtc and last dYts balances according to tjentact and taccount
            string grabCurBalances = "SELECT SUM(dAmount) FROM tjentact WHERE lAcctId=15150000 OR lAcctId=51470000 GROUP BY lAcctId;";

            //set cur vars
            DbReader = dbConReader(grabCurBalances, DbConnection, DbReader, DbCommand);

            curInvBalance = DbReader.GetFieldValue<double>(0);

            DbReader.Read();
            curHAPBalance = DbReader.GetFieldValue<double>(0);


            //Update the field in taccount for Inventory Program account ID 15150000
            string updateTaccountInvProg = "UPDATE taccount SET dtASDate = CURDATE(), tmASTime = CURTIME(), dYtc = dYts +" + curInvBalance
            + " WHERE taccount.lId = 15150000;";

            dbConReader(updateTaccountInvProg, DbConnection, DbReader, DbCommand);



            //Updating taccount at HAP COG program account ID 51470000
            string updateTaccountHapCOG = "UPDATE taccount SET dtASDate = CURDATE(), tmASTime = CURTIME(), dYtc = dYts +" + curHAPBalance
                + " WHERE taccount.lId = 51470000;";


            dbConReader(updateTaccountHapCOG, DbConnection, DbReader, DbCommand);

            DbReader.Close();
            DbConnection.Close();
            DbReader.Dispose();
            DbCommand.Dispose();
        }

        public static void sageTransferUpdate(int qty, string prodName, string locFrom, string locTo, DateTime installDate, DateTime StartDate, DateTime EndDate)
        {

            int primKey;
            int nextPrimKey;
            int locToId;
            int locFromId;
            int inventId;
            string sPartCode;
            double currLocQty;
            double newLocQty;
            double changeInvValuNeg;
            double changeInvValuPos;
            double sageInvValue;
            string uCost = "";
            string lastPrice = "";
            string insertData = "";
            double unitCost = 0;
            double lastP = 0;
            int negQtyMoved = qty * -1;
            double stockVal = 0;
            string sageQty = "";
            string dateBatch = "SAGE IMPORT BATCH " + StartDate.ToString("dd-MMM-yyyy") + " TO " + EndDate.ToString("dd-MMM-yyyy");
            DateTime minDate = DateTime.MinValue;
            
            OdbcDataReader DbReader = null;

            OdbcCommand DbCommand = new OdbcCommand();

            string connectionString = "";

            if (ConfigurationManager.ConnectionStrings["SageConnectionString"] != null)
            {
                connectionString = ConfigurationManager.ConnectionStrings["SageConnectionString"].ConnectionString;
            }




            OdbcConnection DbConnection = new OdbcConnection(connectionString);
            //OdbcConnection DbConnection = new OdbcConnection("DSN=sageLiveTest");
            


            DbConnection.Open();
            //DOUBEL AND TRIPLE CHECK ALL SHITTYCHANGES REVERTED AFTER FAILING TO FIX THIS SHIT

            //Deletes the record that was not finding a match when it gets stuck.....
           // string deleteThisShitFuckinRecordBITCH = "DELETE FROM tinvbyln WHERE lInventId = 249 AND lInvLocId = 92;";
           // DbReader = dbConReader(deleteThisShitFuckinRecordBITCH, DbConnection, DbReader, DbCommand);
            //Deleteing the Inventory ITEM 249 attempt
           // string deleteInvItem = "DELETE FROM tinvent WHERE lId = 249;";
           // DbReader = dbConReader(deleteInvItem, DbConnection, DbReader, DbCommand);
           
            //Insert the deleted again...
          //  string testCRAPinsert = "INSERT INTO tinvbyln (lInventId, lInvLocId, dtASDate, tmASTime, sASUserId, "
          //      + "sASOrgId, dInStock, dCostStk, dOrderPt, dLastCost, dQtyOnOrd, dLastPPrce, dQOnSalOrd, dHInStock,dHCostStk)"
          //      + "VALUES (249,92,'" + minDate.ToString("yyyy-MM-dd") + "', CURTIME(), 'program', 'winsim', " + 1 + "," + 1 + ",0," + 1 + ",0," + 1 + ",0,0,0);";
          //  DbReader = dbConReader(testCRAPinsert, DbConnection, DbReader, DbCommand);
            //Suppose to take away the value that was potentially not matching when doing process transaction.
           // string testDateUpdateSql = "UPDATE tinvbyln SET dtASDate=NULL, tmASTime=NULL WHERE lInventId = 249 AND lInvLocId = 92;";
           // DbReader = dbConReader(testDateUpdateSql, DbConnection, DbReader, DbCommand);
            //All inserts only require primkey lId = 150
            //DbReader.Close();
            //DbConnection.Close();
            //DbReader.Dispose();
            //DbConnection.Dispose();
           // DbCommand.Dispose();
            string titPrimKey = "SELECT lNextId FROM tnxtpids WHERE lId LIKE 150;";

            DbReader = dbConReader(titPrimKey, DbConnection, DbReader, DbCommand);
            primKey = DbReader.GetFieldValue<int>(0);
            nextPrimKey = primKey + 1;

            string updateTitrecPrimKey = "UPDATE tnxtpids SET dtASDate=CURDATE(), tmASTime= CURTIME(),"
                + "sASOrgId='winsim', lNextId=" + nextPrimKey + " WHERE tnxtpids.lId LIKE '150';";

            DbReader = dbConReader(updateTitrecPrimKey, DbConnection, DbReader, DbCommand);

            //Grab location ID 
            string sqlLocFromId = "SELECT lId FROM tinvloc WHERE sGrpCode LIKE '" + locFrom + "';";
            DbReader = dbConReader(sqlLocFromId, DbConnection, DbReader, DbCommand);
            locFromId = DbReader.GetFieldValue<int>(0);
            string sqlLocToId = "SELECT lId FROM tinvloc WHERE sGrpCode LIKE '" + locTo + "';";
            DbReader = dbConReader(sqlLocToId, DbConnection, DbReader, DbCommand);
            locToId = DbReader.GetFieldValue<int>(0);

            //grabs sPartCode and Id from tinvent
            string sqlPartCode = "SELECT sPartCode, lId FROM tinvent WHERE sName LIKE '" + prodName + "';";
            //Set partCode and inventory ID values for SQL inserts/Updates
            DbReader = dbConReader(sqlPartCode, DbConnection, DbReader, DbCommand);

            sPartCode = DbReader.GetFieldValue<string>(0);
            inventId = DbReader.GetFieldValue<int>(1);



            string sqlSageQty = "SELECT dInStock, dLastCost FROM tinvbyln WHERE lInventId = " + inventId + " AND lInvLocId = " + locFromId + "; ";
            DbReader = dbConReader(sqlSageQty, DbConnection, DbReader, DbCommand);
            if (DbReader.HasRows)
            {
                sageQty = DbReader.GetString(0);
                uCost = DbReader.GetString(1);

                currLocQty = Convert.ToDouble(sageQty);
                unitCost = Convert.ToDouble(uCost);

                //New sage QTY to input
                newLocQty = currLocQty - qty;

                //New total sage Inventory value
                sageInvValue = newLocQty * unitCost;
                changeInvValuPos = qty * unitCost;
                changeInvValuNeg = (qty * unitCost) * -1;
                
                string updateTinvbylnFrom = "UPDATE tinvbyln SET dtASDate=CURDATE(), tmASTime=CURTIME(), dInStock = '" + newLocQty + "', dCostStk='" + sageInvValue
                + "' WHERE lInventId = " + inventId + " AND lInvLocId = " + locFromId + ";";


                DbReader = dbConReader(updateTinvbylnFrom, DbConnection, DbReader, DbCommand);

            }
            else
            {
                insertData = "SELECT dLastCost, dLastPPrce FROM tinvbyln WHERE lInventId = " + inventId + ";";

                DbReader = dbConReader(insertData, DbConnection, DbReader, DbCommand);
                uCost = DbReader.GetString(0);
                lastPrice = DbReader.GetString(1);

                unitCost = Convert.ToDouble(uCost);
                lastP = Convert.ToDouble(lastPrice);

                

                stockVal = negQtyMoved * unitCost;
                changeInvValuPos = qty * unitCost;
                changeInvValuNeg = (qty * unitCost) * -1;
          
                string insertTinvbylnFrom = "INSERT INTO tinvbyln (lInventId, lInvLocId, dtASDate, tmASTime, sASUserId, "
                + "sASOrgId, dInStock, dCostStk, dOrderPt, dLastCost, dQtyOnOrd, dLastPPrce, dQOnSalOrd, dHInStock,dHCostStk)"
                + "VALUES (" + inventId + "," + locFromId + ", '" + installDate.ToString("yyyy-MM-dd") + "', CURTIME(), 'program', 'winsim', " + negQtyMoved + "," + stockVal + ",0," + unitCost + ",0," + lastP + ",0,0,0);";
                
                DbReader = dbConReader(insertTinvbylnFrom, DbConnection, DbReader, DbCommand);
            }
            sqlSageQty = "SELECT dInStock, dLastCost FROM tinvbyln WHERE lInventId = " + inventId + " AND lInvLocId = " + locToId + "; ";
            DbReader = dbConReader(sqlSageQty, DbConnection, DbReader, DbCommand);
            if (DbReader.HasRows)
            {

                DbReader = dbConReader(sqlSageQty, DbConnection, DbReader, DbCommand);

                sageQty = DbReader.GetString(0);
                uCost = DbReader.GetString(1);

                currLocQty = Convert.ToDouble(sageQty);
                unitCost = Convert.ToDouble(uCost);

                newLocQty = currLocQty + qty;

                sageInvValue = newLocQty * unitCost;
                changeInvValuPos = qty * unitCost;
                changeInvValuNeg = (qty * unitCost) * -1;

                
                string updateTinvbylnTo = "UPDATE tinvbyln SET dtASDate=CURDATE(), tmASTime=CURTIME(), dInStock = '" + newLocQty + "', dCostStk='" + sageInvValue
                + "' WHERE lInventId = " + inventId + " AND lInvLocId = " + locToId + ";";
               
                DbReader = dbConReader(updateTinvbylnTo, DbConnection, DbReader, DbCommand);
            }
            else
            {
                insertData = "SELECT dLastCost, dLastPPrce FROM tinvbyln WHERE lInventId = " + inventId + ";";
                DbReader = dbConReader(insertData, DbConnection, DbReader, DbCommand);
                uCost = DbReader.GetString(0);
                lastPrice = DbReader.GetString(1);

                unitCost = Convert.ToDouble(uCost);
                lastP = Convert.ToDouble(lastPrice);
                stockVal = qty * unitCost;
                changeInvValuPos = qty * unitCost;
                changeInvValuNeg = (qty * unitCost) * -1;
   
                string insertTinvbylnTo = "INSERT INTO tinvbyln (lInventId, lInvLocId, dtASDate, tmASTime, sASUserId, "
                + "sASOrgId, dInStock, dCostStk, dOrderPt, dLastCost, dQtyOnOrd, dLastPPrce, dQOnSalOrd, dHInStock,dHCostStk)"
                + "VALUES (" + inventId + "," + locToId + ", " + installDate.ToString("yyyy-MM-dd") + ",  CURTIME(), 'program', 'winsim', " + qty + "," + stockVal + ",0," + unitCost + ",0," + lastP + ",0,0,0);";
               
                DbReader = dbConReader(insertTinvbylnTo, DbConnection, DbReader, DbCommand);
            }


            string insertTitLuInvTransfer = "INSERT INTO titlu (lITRecId, dTotal, nTSActSort, lAcctId, bDistByAmt, b40Data, bDeleted, bNotRecd, bFromPO, lAcctDptId, bAlocToAll,nTmplType, lAddrId, nLangPref, bUseVenItm, lProjId)"
           + "VALUES (" + primKey + ", " + changeInvValuNeg + ",0,0,0,0,0,0,0,0,0,0,0,0,0,0);";
            DbReader = dbConReader(insertTitLuInvTransfer, DbConnection, DbReader, DbCommand);

            string insertTitrec = "INSERT INTO titrec (lId,dtASDate, tmASTime, sASOrgId, lVenCusId, lJourId, nTsfIn, nJournal, dtJournal,dtUsing,sComment, dFreight,dInvAmt,fDiscPer,nDiscDay,nNetDay,dDiscAmt,bCashTrans,bCashAccnt,b40Data,"
            + "bReversal,bReversed,bFromPO,bPdByCash,bPdbyCC,bDiscBfTax,bFromImp,bUseMCurr,bLUCleared,bStoreDuty,lCurIdUsed,dExchRate,bPrinted,bEmailed,lChqId,bChallan,bPaidByWeb,nOrdType,bPrePaid,lOrigPPId,lPPId,dPrePAmt,lSoldBy,"
            + "bDSProc,lInvLocId,bTrfLoc,bRmBPLst,bPSPrintd,bPSRmBPLst,lCCTransId,bPdByDP) VALUES(" + primKey + ", '" + installDate.ToString("yyyy-MM-dd") + "', CURTIME(), 'winsim',0,0,0,14,'" + installDate.ToString("yyyy-MM-dd") + "',CURTIME(),'"+ dateBatch +"',0,"
            + "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0);";

            DbReader = dbConReader(insertTitrec, DbConnection, DbReader, DbCommand);

            string insertTitluliOne = "INSERT INTO titluli (lITRecId, nLineNum, sItem, sUnits, nUnitType, sDesc, dAmtInclTx, dOrdered, dRemaining, dPrice, dDutyPer, dDutyAmt, bFreight,"
                + "lTaxCode, lTaxRev, dTaxAmt, bTSLine, dBasePrice, dLineDisc, dVenRel, bVenToStk, bDefDesc) VALUES (" + primKey + ", 1,'" + sPartCode + "', 'Each',"
                + "1, '" + prodName + "'," + changeInvValuNeg + ",0,0," + unitCost + ",0,0,0,0,0,0,0,0,0,0,1,1);";
            string insertTitluliTwo = "INSERT INTO titluli (lITRecId, nLineNum, sItem, sUnits, nUnitType, sDesc, dAmtInclTx, dOrdered, dRemaining, dPrice, dDutyPer, dDutyAmt, bFreight,"
                + "lTaxCode, lTaxRev, dTaxAmt, bTSLine, dBasePrice, dLineDisc, dVenRel, bVenToStk, bDefDesc) VALUES (" + primKey + ", 2,'" + sPartCode + "', 'Each',"
                + "1, '" + prodName + "'," + changeInvValuPos + ",0,0," + unitCost + ",0,0,0,0,0,0,0,0,0,0,0,0);";

            //insert new titluli rec
            DbReader = dbConReader(insertTitluliOne, DbConnection, DbReader, DbCommand);
            DbReader = dbConReader(insertTitluliTwo, DbConnection, DbReader, DbCommand);

            //Insert new record into titrline uses tirec prime key
            string insertTitrlineOne = "INSERT INTO titrline (lITRecId, nLineNum, sSource, lInventId, lAcctId, dQty, dPrice, dAmt, dCost, dRev,bTsfIn, bVarLine, bReversal, bService,"
                + "lAcctDptId,lInvLocId, bDefPrc,lPrcListId,dBasePrice,dLineDisc,bDefBsPric,bDelInv,bUseVenItm) VALUES ("
                + primKey + ", 1, '" + sPartCode + "'," + inventId + ", 15150000, " + negQtyMoved + ", " + unitCost + ", " + changeInvValuNeg + "," + changeInvValuNeg + ",0,0,0,0,0,0," + locFromId + ",0,0,0,0,0,0,0);";
            string insertTitrlineTwo = "INSERT INTO titrline (lITRecId, nLineNum, sSource, lInventId, lAcctId, dQty, dPrice, dAmt, dCost, dRev,bTsfIn, bVarLine, bReversal, bService,"
                + "lAcctDptId,lInvLocId, bDefPrc,lPrcListId,dBasePrice,dLineDisc,bDefBsPric,bDelInv,bUseVenItm) VALUES ("
                + primKey + ", 2, '" + sPartCode + "'," + inventId + ", 15150000, " + qty + ", " + unitCost + ", " + changeInvValuPos + "," + changeInvValuPos + ",0,0,0,0,0,0," + locToId + ",0,0,0,0,0,0,0);";
            //insert new titrline rec
            DbReader = dbConReader(insertTitrlineOne, DbConnection, DbReader, DbCommand);
            DbReader = dbConReader(insertTitrlineTwo, DbConnection, DbReader, DbCommand);
            //update the tinvent table with new times according to prodName
            string updateTinvent = "UPDATE tinvent SET dtASDate = CURDATE(), tmASTime=CURTIME() WHERE lId =" + inventId + ";";

            DbReader = dbConReader(updateTinvent, DbConnection, DbReader, DbCommand);

            string updateTbstat = "UPDATE ttbstat SET dtASDate=CURDATE(),tmASTime=CURTIME(),sASUserId='sysadmin',sASOrgId='winsim' WHERE lId=90;";
            DbReader = dbConReader(updateTbstat, DbConnection, DbReader, DbCommand);

            string updateTinvlocFrom = "UPDATE tinvloc SET bUsed = 1 WHERE lId = " + locFromId + "";
            string updateTinvlocTo = "UPDATE tinvloc SET bUsed = 1 WHERE lId = " + locToId + "";
            DbReader = dbConReader(updateTinvlocFrom, DbConnection, DbReader, DbCommand);
            DbReader = dbConReader(updateTinvlocTo, DbConnection, DbReader, DbCommand);
            

            DbReader.Close();
            DbConnection.Close();
            DbReader.Dispose();
            DbConnection.Dispose();
            DbCommand.Dispose();

        }

        public static OdbcDataReader dbConReader(string sqlState, OdbcConnection DbConnection, OdbcDataReader DbReader, OdbcCommand DbCommand)
        {
            try
            {
                DbCommand = DbConnection.CreateCommand();

                DbCommand.CommandText = sqlState;
                
                DbReader = DbCommand.ExecuteReader();
                //System.Threading.Thread.Sleep(250);
                DbReader.Read();

                //DbCommand.Dispose();

                return DbReader;

            }
            catch (Exception e)
            {
                return null;
            }
        }


    }

    public class ConsolidatedInventory
    {
        public FastFileAppliance Appliance { get; set; }
        public Location LocationTo { get; set; }
        public List<Move> Moves { get; set; }


        //public string FavoriteColor { get; set; }
        //public List<Child> Children { get; set; }
    }

}
