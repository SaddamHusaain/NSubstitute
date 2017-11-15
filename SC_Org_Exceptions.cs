using Core.SYS_Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreInterfaces;
using Core.DataContracts.Responses;
using Core.DatabaseOps;
using Core.SYS_Enums;
using Core.DataContracts.Requests;
using Core.SYS_Objects;
using Core.Configuration;
using DotNetClassExtensions;
using Core.SYS_Interfaces;
using NodaTime;
using NodaTime.Text;
using CoreDataContracts;
using CoreObjects;

namespace SCImplementations
{
    public class SC_Org_Exceptions : ISC_Org_Exceptions
    {
        public IDCR_Added Create_Org_Exception(IDcCreateException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Added resp = new DCR_Added();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcCreateException)))
            {
                if (!validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_createOrgException))
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                //check to see if we can add the exception if we cant add that no point checking anything else
                if (request_obj.resourceIdList.Count == 0 && request_obj.calendarIdList.Count == 0)
                {
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                #region validation routines
                DateTimeZone exceptionTimeZone = DateTimeZoneProviders.Tzdb[request_obj.timeZoneIANA];
                Instant startInstant = Instant.FromDateTimeUtc(DateTime.Parse(request_obj.start).ToUniversalTime());
                ZonedDateTime start = new ZonedDateTime(startInstant, exceptionTimeZone);
                Instant endInstant = Instant.FromDateTimeUtc(DateTime.Parse(request_obj.end).ToUniversalTime());
                ZonedDateTime end = new ZonedDateTime(endInstant, exceptionTimeZone);

                IInstantStartStop tre = coreFactory.InstantStartStop();
                tre.start = InstantPattern.ExtendedIsoPattern.Parse(request_obj.start).Value;
                tre.stop = InstantPattern.ExtendedIsoPattern.Parse(request_obj.end).Value;

                Instant futureMaxRange = InstantPattern.ExtendedIsoPattern.Parse(request_obj.end).Value;

                foreach (IRepeatOptions repeatRule in request_obj.repeatRuleOptions)
                {
                    Instant repeatRuleEndInstant = InstantPattern.ExtendedIsoPattern.Parse(request_obj.end).Value;
                    if (futureMaxRange < repeatRuleEndInstant)
                    {
                        futureMaxRange = repeatRuleEndInstant;
                    }
                }

                #region check that the request doesnt have calendars and resource id's specified
                if (request_obj.resourceIdList.Count > 0 && request_obj.calendarIdList.Count > 0)
                {
                    //this is invalid
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                #endregion

                #region generate all the new time scale objs
                //List<BaseInstantStartStop> completeNewBaseInstantList = new List<BaseInstantStartStop>();
                //completeNewBaseInstantList.Add(tre);
                ////check to see if there are any repeat rules
                //List<BaseInstantStartStop> listOfRepeatRuleTimePeriods = new List<BaseInstantStartStop>();
                IList<IInstantStartStop> completeNewBaseInstantList = coreFactory.ListInstantStartStop();
                completeNewBaseInstantList.Add(tre);
                //check to see if there are any repeat rules
                IList<IInstantStartStop> listOfRepeatRuleTimePeriods = coreFactory.ListInstantStartStop();
                for (int i = 0; i < request_obj.repeatRuleOptions.Count; i++)
                {
                    //if there are repeat rules then generate the repeated time periods
                    ITimeStartEnd trange = coreFactory.TimeStartEnd();
                    trange.start = request_obj.start;
                    trange.end = request_obj.end;

                    List<IInstantStartStop> fullTsoList = utils.GenerateRepeatTimePeriods(request_obj.coreProj, coreSc, trange, request_obj.repeatRuleOptions[i], request_obj.timeZoneIANA, true, coreDb, coreFactory);
                    //if (fullTsoList.func_status != ENUM_Cmd_Status.ok)
                    //{
                    //    resp.StatusAndMessage_CopyFrom(fullTsoList);
                    //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    //    return resp;
                    //}
                    //listOfRepeatRuleTimePeriods.AddRange(fullTsoList.TimePeriods);
                    //completeNewBaseInstantList.AddRange(fullTsoList.TimePeriods);
                    listOfRepeatRuleTimePeriods.AddRange(fullTsoList);
                    completeNewBaseInstantList.AddRange(fullTsoList);
                }
                #endregion
                #region read all the calendars linked to the exception
                //Dictionary<int, List<BaseTSo>> currentCalendarTso = new Dictionary<int, List<BaseTSo>>();
                Dictionary<int, IList<ITSO>> currentCalendarTso = new Dictionary<int, IList<ITSO>>();

                //Dictionary<int, BaseOrgCalendar> calendarDetailList = new Dictionary<int, BaseOrgCalendar>();
                Dictionary<int, ICalendar> calendarDetailList = new Dictionary<int, ICalendar>();

                //this is a complex structure it consists of the caledar id, then a list of the resource ids which also contain the time periods as well
                //Dictionary<int, Dictionary<int, List<BaseTSo>>> calendarResourceTSOMaps = new Dictionary<int, Dictionary<int, List<BaseTSo>>>();
                Dictionary<int, Dictionary<int, IList<ITSO>>> calendarResourceTSOMaps = new Dictionary<int, Dictionary<int, IList<ITSO>>>();

                //List<BaseTSo> completeAlreadyAllocatedTimes = new List<BaseTSo>();
                IList<ITSO> completeAlreadyAllocatedTimes = coreFactory.ListITSO();
                //List<BaseTSo> TSOsWhichCanConflict = new List<BaseTSo>();
                IList<ITSO> TSOsWhichCanConflict = coreFactory.ListITSO();
                foreach (int calendarId in request_obj.calendarIdList)
                {
                    #region read the calendar details
                    IDcCalendarId dcCalendarId = coreFactory.DcCalendarId(request_obj.coreProj);
                    dcCalendarId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcCalendarId.orgId = request_obj.orgId;
                    dcCalendarId.calendarId = calendarId;
                    //DCR_Org_Calendar calendarData = SC_Org_Calendars.Read_Org_Calendar_By_Calendar_ID(dcCalendarId);
                    IDcrCalendar calendarData = coreSc.Read_Org_Calendar_By_Calendar_ID(dcCalendarId, validation, utils, coreSc, coreDb, coreFactory);
                    if (calendarData.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.StatusAndMessage_CopyFrom(calendarData);
                        return resp;
                    }
                    ICalendar calendarDetails = coreFactory.Calendar();
                    calendarDetailList.Add(calendarData.calendarId, calendarDetails);
                    #endregion
                    #region get the calendar tsos
                    IDcCalendarTimeRange calendarIdRequest = coreFactory.DcCalendarTimeRange(request_obj.coreProj);
                    calendarIdRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    calendarIdRequest.orgId = request_obj.orgId;
                    calendarIdRequest.calendarId = calendarId;
                    calendarIdRequest.start = request_obj.start;
                    calendarIdRequest.end = futureMaxRange.ToDateTimeUtc().ISO8601Str();

                    IDcrTsoList calendarTSos = coreSc.Read_TimePeriods_For_Calendar_Between_DateTime(calendarIdRequest, validation, utils,  coreSc, coreDb,coreFactory);
                    if (calendarTSos.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.StatusAndMessage_CopyFrom(calendarTSos);
                        return resp;
                    }
                    //currentCalendarTso.Add(calendarId, calendarTSos.timeScaleList);
                    currentCalendarTso.Add(calendarId, calendarTSos.timeScaleList);
                    #endregion
                    #region store the calendartsos into the main already allocated list of tsos
                    completeAlreadyAllocatedTimes.AddRange(calendarTSos.timeScaleList);
                    TSOsWhichCanConflict.AddRange(calendarTSos.timeScaleList);
                    #endregion
                    #region find the resources linked to the current looped calendar and get all of there tso's into lists

                    IDcrIdList calendarResMaps = coreSc.Read_All_Org_Calendar_Resource_Mappings_By_Calendar_ID(dcCalendarId, validation, utils, coreSc, coreDb,coreFactory);
                    if (calendarResMaps.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.StatusAndMessage_CopyFrom(calendarResMaps);
                        return resp;
                    }
                    foreach (int resourceId in calendarResMaps.ListOfIDs)
                    {
                        IDcOrgResourceId resourceIdObj = coreFactory.DcOrgResourceId(request_obj.coreProj);
                        resourceIdObj.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        resourceIdObj.orgId = request_obj.orgId;
                        resourceIdObj.resourceId = resourceId;
                        IDcrResourceComplete resourceDetails = coreSc.Read_Resource_By_ID(resourceIdObj, validation, utils, coreSc, coreDb,coreFactory);
                        if (resourceDetails.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(resourceDetails);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }

                        IDCResourceTimeRange resourceTimeRange = coreFactory.DCResourceTimeRange(request_obj.coreProj);
                        resourceTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        resourceTimeRange.orgId = request_obj.orgId;
                        resourceTimeRange.resourceId = resourceId;
                        resourceTimeRange.start = request_obj.start;
                        resourceTimeRange.end = futureMaxRange.ToDateTimeUtc().ISO8601Str();
                        IDcrTsoList resourceTSOs = coreSc.Read_TimePeriods_For_Resource_Between_DateTime(resourceTimeRange, validation, utils, coreSc, coreDb,coreFactory);

                        if (resourceTSOs.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(resourceTSOs);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        //Dictionary<int, List<BaseTSo>> resTsoDic = new Dictionary<int, List<BaseTSo>>();
                        Dictionary<int, IList<ITSO>> resTsoDic = new Dictionary<int, IList<ITSO>>();
                        resTsoDic.Add(resourceId, resourceTSOs.timeScaleList);
                        if (calendarResourceTSOMaps.ContainsKey(calendarId))
                        {
                            calendarResourceTSOMaps[calendarId].Union(resTsoDic);
                        }
                        else
                        {
                            calendarResourceTSOMaps.Add(calendarId, resTsoDic);
                        }

                        #region store the calendar resource tsos into the main allocated tso list
                        completeAlreadyAllocatedTimes.AddRange(resourceTSOs.timeScaleList);
                        if (resourceDetails.allowsOverlaps != Enum_SYS_BookingOverlap.OverLappingAllowed)
                        {
                            TSOsWhichCanConflict.AddRange(resourceTSOs.timeScaleList);
                        }
                        #endregion
                    }
                    #endregion
                }
                #endregion

                #region read all the resources and current resource time periods
                //loop all the resources and collect the current time periods and resource data
                //Dictionary<int, List<BaseTSo>> currentResourceTso = new Dictionary<int, List<BaseTSo>>();
                Dictionary<int, IList<ITSO>> currentResourceTso = new Dictionary<int, IList<ITSO>>();
                Dictionary<int, IResourceComplete> resourceDetailList = new Dictionary<int, IResourceComplete>();
                foreach (int resourceId in request_obj.resourceIdList)
                {
                    IDcOrgResourceId dcResourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);
                    dcResourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcResourceId.orgId = request_obj.orgId;
                    dcResourceId.resourceId = resourceId;

                    IDcrResourceComplete resourceData = coreSc.Read_Resource_By_ID(dcResourceId, validation, utils, coreSc, coreDb,coreFactory);
                    if (resourceData.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.StatusAndMessage_CopyFrom(resourceData);
                        return resp;
                    }
                    IResourceComplete resourceDetails = coreFactory.ResourceComplete();
                    resourceDetailList.Add(resourceData.resourceId, resourceDetails);
                    IDCResourceTimeRange resourceIdRequest = coreFactory.DCResourceTimeRange(request_obj.coreProj);
                    resourceIdRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    resourceIdRequest.orgId = request_obj.orgId;
                    resourceIdRequest.resourceId = resourceId;
                    resourceIdRequest.start = request_obj.start;
                    resourceIdRequest.end = futureMaxRange.ToDateTimeUtc().ISO8601Str();

                    IDcrTsoList resourceTSos = coreSc.Read_TimePeriods_For_Resource_Between_DateTime(resourceIdRequest, validation, utils, coreSc, coreDb,coreFactory);

                    if (resourceTSos.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.StatusAndMessage_CopyFrom(resourceTSos);
                        return resp;
                    }

                    currentResourceTso.Add(resourceId, resourceTSos.timeScaleList);
                    completeAlreadyAllocatedTimes.AddRange(resourceTSos.timeScaleList);
                    if (resourceData.allowsOverlaps != Enum_SYS_BookingOverlap.OverLappingAllowed)
                    {
                        TSOsWhichCanConflict.AddRange(resourceTSos.timeScaleList);
                    }
                }
                #endregion

                #region check for conflicts in the newly generated lists and the old lists
                //List<BaseTSo> conflictList = Utils.GetConflictingTimePeriods(TSOsWhichCanConflict, completeNewBaseInstantList);
                List<ITSO> conflictList = utils.GetConflictingTimePeriods(TSOsWhichCanConflict, completeNewBaseInstantList);
                #endregion
                #region if no conflicts proceed otherwise return 
                if (conflictList.Count != 0)
                {
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                #endregion

                #region check to see if it will be a resource exception or whether it will be a calendar exception as it cant be both
                if (request_obj.resourceIdList.Count > 0)
                {
                    //its a resource exception check there are no calendars currently mapped
                    if (calendarDetailList.Count > 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                }
                else if (request_obj.calendarIdList.Count > 0)
                {
                    //its a calendar exception check there are no resources currently mapped
                    if (resourceDetailList.Count > 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                }
                else
                {
                    //its not a resource or calendar exception so will not map it to any so that means we dont need to make any checks
                }
                #endregion

                #endregion
                //if everything is ok and we have made it this far we now need create and generate the timescaleobj entries
                #region firstly generate the exception
                INewRecordId out_new_exception_id = coreFactory.NewRecordId();

                if (coreDb.Create_Exception(request_obj.coreProj, request_obj,  out_new_exception_id) != ENUM_DB_Status.DB_SUCCESS)
                {
                    resp.SetResponseServerError();
                    resp.NewRecordID = -1;
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                #endregion
                #region generate the time period
                IDcCreateTSO createTSO = coreFactory.DcCreateTSO(request_obj.coreProj);
                createTSO.exceptionId = out_new_exception_id.NewRecordID;
                createTSO.calendarIdList = new List<int>();
                createTSO.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                createTSO.dateOfGeneration = DateTime.Now.OrijDTStr();
                createTSO.durationMilliseconds = request_obj.durationMilliseconds;
                createTSO.end = request_obj.end;
                createTSO.appointmentId = 0;
                createTSO.orgId = request_obj.orgId;
                createTSO.repeatId = 0;
                createTSO.resourceIdList = new List<int>();
                createTSO.start = request_obj.start;
                //DCR_Added createdTSO = SC_TSO.Create_TimePeriod(createTSO);
                IList<ITimeStartEnd> listTimeStartEnd = coreFactory.ListInstantStartEnd();
                IDCR_Added createdTSO = coreSc.Create_TimePeriod(createTSO, validation, utils , coreSc, coreDb,coreFactory);

                if (createdTSO.func_status != ENUM_Cmd_Status.ok)
                {
                    resp.StatusAndMessage_CopyFrom(createdTSO);
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
                #endregion
                #region link the exception to the resources
                //then generate the map from exception to resource
                foreach (int resourceId in request_obj.resourceIdList)
                {
                    IDcResourceException createOrgExceptionMap = coreFactory.DcResourceException(request_obj.coreProj);
                    createOrgExceptionMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createOrgExceptionMap.exceptionId = out_new_exception_id.NewRecordID;
                    createOrgExceptionMap.orgId = request_obj.orgId;
                    createOrgExceptionMap.resourceId = resourceId;

                    IDCR_Added addedMap = coreSc.Create_Org_Exception_Resource_Mapping(createOrgExceptionMap, validation, utils, coreSc, coreDb, coreFactory);
                    if (addedMap.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(addedMap);
                        resp.NewRecordID = -1;
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                }
                #endregion
                #region link the exception to the calendar
                foreach (int calendarId in request_obj.calendarIdList)
                {
                    IDcCalendarExceptionId createOrgExceptionMap = coreFactory.DcCalendarExceptionId(request_obj.coreProj);
                    createOrgExceptionMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createOrgExceptionMap.exceptionId = out_new_exception_id.NewRecordID;
                    createOrgExceptionMap.orgId = request_obj.orgId;
                    createOrgExceptionMap.calendarId = calendarId;
                    //DCR_Added addedMap = SC_Org_Exceptions.Create_Org_Exception_Calendar_Mapping(createOrgExceptionMap);
                    IDCR_Added addedMap = coreSc.Create_Org_Exception_Calendar_Mapping(createOrgExceptionMap, validation, utils,coreSc, coreDb,coreFactory);
                    if (addedMap.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(addedMap);
                        resp.NewRecordID = -1;
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                }
                #endregion
                #region create all the repeat objects
                //this creates all the repeat rule objects
                for (int i = 0; i < request_obj.repeatRuleOptions.Count; i++)
                {
                    IDcCreateRepeat createRepeatRule = coreFactory.DcCreateRepeat(request_obj.coreProj);
                    createRepeatRule.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createRepeatRule.creatorId = request_obj.cmd_user_id;
                    createRepeatRule.end = request_obj.repeatRuleOptions[i].end;
                    createRepeatRule.maxOccurances = request_obj.repeatRuleOptions[i].maxOccurances;
                    createRepeatRule.orgId = request_obj.repeatRuleOptions[i].orgId;
                    createRepeatRule.repeatDay = request_obj.repeatRuleOptions[i].repeatDay;
                    createRepeatRule.repeatType = request_obj.repeatRuleOptions[i].repeatType;
                    createRepeatRule.repeatMonth = request_obj.repeatRuleOptions[i].repeatMonth;
                    createRepeatRule.repeatWeek = request_obj.repeatRuleOptions[i].repeatWeek;
                    createRepeatRule.repeatWeekDays = request_obj.repeatRuleOptions[i].repeatWeekDays;
                    createRepeatRule.repeatYear = request_obj.repeatRuleOptions[i].repeatYear;
                    createRepeatRule.start = request_obj.repeatRuleOptions[i].start;
                    createRepeatRule.modifier = request_obj.repeatRuleOptions[i].modifier;

                    IDCR_Added repeatAdded = coreSc.Create_Repeat(createRepeatRule, validation, utils, coreSc, coreDb,coreFactory);
                    if (repeatAdded.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(repeatAdded);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #region link the exception to the repeat object, this should also generate the extra time scale objs
                    IDcMapRepeatException createAppRepMap = coreFactory.DcMapRepeatException(request_obj.coreProj);
                    createAppRepMap.exceptionId = out_new_exception_id.NewRecordID;
                    createAppRepMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createAppRepMap.creatorId = request_obj.cmd_user_id;
                    createAppRepMap.orgId = request_obj.orgId;
                    createAppRepMap.repeatId = repeatAdded.NewRecordID;

                    IDCR_Added createdAppRepMap = coreSc.Create_Org_Exception_Repeat_Map(createAppRepMap, validation, utils,coreSc, coreDb,coreFactory);
                    if (createdAppRepMap.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(createdAppRepMap);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                }
                #endregion


                resp.Result = ENUM_Cmd_Add_Result.Added;
                resp.NewRecordID = out_new_exception_id.NewRecordID;
                resp.SetResponseOk();
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
            }
            return resp;
        }

        public IDCR_Added Create_Org_Exception_Calendar_Mapping(IDcCalendarExceptionId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
           
            DCR_Added resp = new DCR_Added();

            DateTime startTime = DateTime.Now;

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcCalendarExceptionId)))
            {

                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_createCalendarExceptionMapping))
                {
                    #region read the exception details
                    IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                    exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    exceptionId.orgId = request_obj.orgId;
                    exceptionId.exceptionId = request_obj.exceptionId;

                    IDcrException exceptionData = coreSc.Read_Org_Exception_By_Exception_ID(exceptionId, validation, utils,  coreSc, coreDb,coreFactory);
                    if (exceptionData.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionData);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }
                    #endregion
                    #region read all the resources to the exception should be 0

                    IDcrIdList exceptionResourceIds = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(exceptionId, validation, utils,coreSc, coreDb,coreFactory);
                    if (exceptionResourceIds.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionResourceIds);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    if (exceptionResourceIds.ListOfIDs.Count > 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region read all the calendars mapped to the exception

                    IDcrIdList calendarsAlreadyMapped = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(exceptionId, validation, utils, coreSc, coreDb,coreFactory);
                    if (calendarsAlreadyMapped.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(calendarsAlreadyMapped);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }

                    #endregion
                    #region check to see if the calendar is already mapped to the exception
                    if (calendarsAlreadyMapped.ListOfIDs.Contains(request_obj.calendarId))
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Already_Added;
                        resp.SetResponseOk();

                        return resp;
                    }
                    #endregion
                    #region read the calendar which will be mapped
                    IDcCalendarId dc_o_r_i = coreFactory.DcCalendarId(request_obj.coreProj);
                    dc_o_r_i.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dc_o_r_i.orgId = request_obj.orgId;
                    dc_o_r_i.calendarId = request_obj.calendarId;

                    IDcrCalendar calendarDetails = coreSc.Read_Org_Calendar_By_Calendar_ID(dc_o_r_i, validation, utils, coreSc, coreDb, coreFactory);
                    if (calendarDetails.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(calendarDetails);
                        resp.NewRecordID = -1;
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }
                    #endregion
                    #region read TSO's already generated and mapped to the exception

                    IDcrTsoList exceptionTSOs = coreSc.Read_All_TimePeriods_For_Exception(exceptionId, validation, utils, coreSc, coreDb,coreFactory);

                    if (exceptionTSOs.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionTSOs);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }

                    IList<IInstantStartStop> currentExceptionTimePeriods = coreFactory.ListInstantStartStop();
                    foreach (ITSO tso in exceptionTSOs.timeScaleList)
                    {
                        IInstantStartStop tr = coreFactory.InstantStartStop();
                        tr.start = InstantPattern.ExtendedIsoPattern.Parse(tso.start).Value;
                        tr.stop = InstantPattern.ExtendedIsoPattern.Parse(tso.end).Value;
                        currentExceptionTimePeriods.Add(tr);
                    }
                    #endregion
                    #region read the org details
                    IDcOrgId orgId = coreFactory.DcOrgId(request_obj.coreProj);
                    orgId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    orgId.orgId = request_obj.orgId;

                    IDcrOrg orgDetails = coreSc.Read_Org_By_Org_ID(orgId, validation, utils, coreSc, coreDb,coreFactory);

                    if (orgDetails.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(orgDetails);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }
                    #endregion
                    #region read all the resources mapped to the calendar
                    IDcCalendarId calendarId = coreFactory.DcCalendarId(request_obj.coreProj);
                    calendarId.calendarId = request_obj.calendarId;
                    calendarId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    calendarId.orgId = request_obj.orgId;

                    IDcrIdList calendarResourceIds = coreSc.Read_All_Org_Calendar_Resource_Mappings_By_Calendar_ID(calendarId, validation, utils, coreSc, coreDb,coreFactory);
                    if (calendarResourceIds.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(calendarResourceIds);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region loop all the resources linked to the calendar 
                    Dictionary<int, IList<IInstantStartStop>> resourceTSOs = new Dictionary<int, IList<IInstantStartStop>>();
                    Dictionary<int, List<ITSO>> resourceTSOObjs = new Dictionary<int, List<ITSO>>();
                    IDcOrgResourceId resourceIdRequest = coreFactory.DcOrgResourceId(request_obj.coreProj);
                    resourceIdRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    resourceIdRequest.orgId = request_obj.orgId;
                    foreach (int resourceId in calendarResourceIds.ListOfIDs)
                    {
                        resourceIdRequest.resourceId = resourceId;
                        IList<IInstantStartStop> resTSOS = coreFactory.ListInstantStartStop();
                        resourceTSOs.Add(resourceId, resTSOS);
                        resourceTSOObjs.Add(resourceId, new List<ITSO>());

                        IDcrTsoList tsosForResource = coreSc.Read_All_TimePeriods_For_Resource(resourceIdRequest, validation, utils, coreSc, coreDb,coreFactory);

                        if (tsosForResource.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(tsosForResource);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        foreach (ITSO tsoDetails in tsosForResource.timeScaleList)
                        {
                            if (tsoDetails.exceptionId != request_obj.exceptionId)
                            {
                                IInstantStartStop tr = coreFactory.InstantStartStop();
                                tr.start = InstantPattern.ExtendedIsoPattern.Parse(tsoDetails.start).Value;
                                tr.stop = InstantPattern.ExtendedIsoPattern.Parse(tsoDetails.end).Value;
                                resourceTSOs[resourceId].Add(tr);
                                resourceTSOObjs[resourceId].Add(tsoDetails);
                            }
                        }
                    }
                    #endregion
                    #region read all the tsos already linked to the calendar

                    IDcrTsoList calendarTSOs = coreSc.Read_All_TimePeriods_For_Calendar(calendarId, validation, utils, coreSc, coreDb,coreFactory);
                    if (calendarTSOs.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(calendarTSOs);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion

                    #region exception timerange
                    IInstantStartStop exceptionTr = coreFactory.InstantStartStop();
                    exceptionTr.start = InstantPattern.ExtendedIsoPattern.Parse(exceptionData.start).Value;
                    exceptionTr.stop = InstantPattern.ExtendedIsoPattern.Parse(exceptionData.end).Value;
                    #endregion


                    IList<IInstantStartStop> ListIInstantStartStop = coreFactory.ListInstantStartStop();
                    #region test the resource TSOS for conflict
                    foreach (KeyValuePair<int, IList<IInstantStartStop>> entry in resourceTSOs)
                    {
                        //DCR_TimePeriod_List timeperiodList = Utils.GetConflictingTimePeriods(exceptionTr, entry.Value);
                        List<IInstantStartStop> timeperiodList = utils.GetConflictingTimePeriods(exceptionTr, ListIInstantStartStop);
                        //if (timeperiodList.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(timeperiodList);
                        //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        //    return resp;
                        //}
                        //if (timeperiodList.TimePeriods.Count > 0)
                        if (timeperiodList.Count > 0)
                        {
                            resp.SetResponseInvalidParameter();
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                    }
                    #endregion

                    #region test the calendar TSOS for conflicts
                    //DCR_TimePeriod_List calendarConflicts = Utils.GetConflictingTimePeriods(exceptionTr, calendarTSOs.timeScaleList);
                    List<IInstantStartStop> calendarConflicts = utils.GetConflictingTimePeriods(exceptionTr, calendarTSOs.timeScaleList);
                    //if (calendarConflicts.func_status != ENUM_Cmd_Status.ok)
                    //{
                    //    resp.StatusAndMessage_CopyFrom(calendarConflicts);
                    //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    //    return resp;
                    //}
                    //if (calendarConflicts.TimePeriods.Count > 0)
                    if (calendarConflicts.Count > 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion

                    #region create the link for the exception + calendar in the db
                    IDcCalendarsTSOs calExTsoMap = coreFactory.DcCalendarsTSOs(request_obj.coreProj);
                    calExTsoMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //foreach (BaseTSo tso in exceptionTSOs.timeScaleList)
                    foreach (ITSO tso in exceptionTSOs.timeScaleList)
                    {
                        ITsoOrgCalendarId baseTSOOrgCalId = coreFactory.TsoOrgCalendarId();
                        baseTSOOrgCalId.orgId = request_obj.orgId;
                        baseTSOOrgCalId.calendarId = request_obj.calendarId;
                        baseTSOOrgCalId.tsoId = tso.tsoId;
                        calExTsoMap.listOfTSOOrgCalendarIds.Add(baseTSOOrgCalId);
                    }
                    

                    List<ITimeStartEnd> ListITimeStartEnd = new List<ITimeStartEnd>();

                    IDcrAddedList createdTsoResMap = coreSc.Create_TimePeriod_Calendar_Maps(calExTsoMap, validation, utils,  coreSc, coreDb,coreFactory);

                    if (createdTsoResMap.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(createdTsoResMap);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;

                        return resp;
                    }
                    INewRecordId newMappingID = coreFactory.NewRecordId();
                    if (coreDb.Create_Calendar_Exception_Mapping(request_obj.coreProj,
                            request_obj, request_obj, newMappingID) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.NewRecordID = newMappingID.NewRecordID;
                        resp.Result = ENUM_Cmd_Add_Result.Added;
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.NewRecordID = -1;
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.SetResponseServerError();
                    }
                    #endregion
                }
                else
                {
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }
        
        public IDcrAddedList Create_Org_Exception_Repeats_Mapping(IDcMapRepeatsException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_AddedList resp = new DCR_AddedList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory,request_obj, typeof(IDcMapRepeatsException)) && request_obj.repeatIds.Count > 0)
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_createOrgExceptionRepeatMap))
                {
                    //read the exceptiond details
                    //read the exception data
                    
                    IDcExceptionID exceptionRequest = coreFactory.DcExceptionID(request_obj.coreProj);
                    exceptionRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    exceptionRequest.exceptionId = request_obj.exceptionId;
                    exceptionRequest.orgId = request_obj.orgId;

                    IDcrException exceptionDetails = coreSc.Read_Org_Exception_By_Exception_ID(exceptionRequest, validation, utils, coreSc, coreDb,coreFactory);

                    IDcrIdList listOfResourcesMappedToException = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(exceptionRequest, validation, utils, coreSc, coreDb, coreFactory);

                    
                    if (listOfResourcesMappedToException.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(listOfResourcesMappedToException);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }

                    //Dictionary<int, List<BaseInstantStartStop>> repeatTimePeriods = new Dictionary<int, List<BaseInstantStartStop>>();
                    Dictionary<int, List<IInstantStartStop>> repeatTimePeriods = new Dictionary<int, List<IInstantStartStop>>();
                    foreach (int repeatId in request_obj.repeatIds)
                    {
                        //loop the repeats
                        //read the repeat data
                        IDcRepeatId dcRepeatId = coreFactory.DcRepeatId(request_obj.coreProj);
                        dcRepeatId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcRepeatId.orgId = request_obj.orgId;
                        dcRepeatId.repeatId = repeatId;

                        
                        IDcrRepeat repeatDetails = coreSc.Read_Repeat(dcRepeatId, validation, utils, coreSc, coreDb, coreFactory);

                        if (repeatDetails.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(repeatDetails);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        //the repeat rule shouldnt really be allowed to have a start date for before the exception
                        if (DateTime.Parse(repeatDetails.end) < DateTime.Parse(exceptionDetails.start))
                        {
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            resp.NewRecordIDs.Clear();
                            resp.SetResponsePermissionDenied();
                            return resp;
                        }
                        //create a tmp copy of the repeated events for the system configured boundary
                        ITimeStartEnd eventStartStop = coreFactory.TimeStartEnd();
                        eventStartStop.start = DateTime.Parse(exceptionDetails.start).ToUniversalTime().ISO8601Str();
                        eventStartStop.end = DateTime.Parse(exceptionDetails.end).ToUniversalTime().ISO8601Str();
                        //DCR_Org_TimePeriod_List generatedRepeatTimePeriods = new DCR_Org_TimePeriod_List();
                        //DCR_OrgTimePeriodList generatedRepeatTimePeriods = new DCR_OrgTimePeriodList();
                        //List<IInstantStartStop> generatedRepeatTimePeriods = new List<IInstantStartStop>();
                        //IRepeatOptions repeatOptions = coreFactory.RepeatOptions();


                        List<IInstantStartStop> generatedRepeatTimePeriods = utils.GenerateRepeatTimePeriods(request_obj.coreProj, coreSc, eventStartStop, repeatDetails, exceptionDetails.timeZoneIANA, true, coreDb, coreFactory);

                        //repeatTimePeriods.Add(repeatId, generatedRepeatTimePeriods.TimePeriods);
                        repeatTimePeriods.Add(repeatId, generatedRepeatTimePeriods);

                        //TODO: fix bug here due things going over the year boundary from today.....................
                        ////if (generatedRepeatTimePeriods.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(generatedRepeatTimePeriods[0]);
                        //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        //    return resp;
                        //}

                        foreach (int resourceId in listOfResourcesMappedToException.ListOfIDs)
                        {
                            IDCResourceTimeRange resourceTimeRange = coreFactory.DCResourceTimeRange(request_obj.coreProj);
                            resourceTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            resourceTimeRange.orgId = request_obj.orgId;
                            resourceTimeRange.resourceId = resourceId;
                            resourceTimeRange.start = DateTime.Parse(exceptionDetails.start).OrijDTStr();
                            Instant tmpI = InstantPattern.ExtendedIsoPattern.Parse(exceptionDetails.start).Value.Plus(Duration.FromStandardDays(GeneralConfig.MAX_GENERATE_FUTURE_IN_DAYS));
                            resourceTimeRange.end = tmpI.ToDateTimeUtc().EndOfDayUTC().ISO8601Str();
                            //test to see if any of the resources have tso which conflict with the new exceptions

                            IDcrIdList readTSoIds = coreSc.Read_Resource_TSOs_By_Resource_ID_TimeRange(resourceTimeRange, validation, utils, coreSc, coreDb,coreFactory);

                            if (readTSoIds.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(readTSoIds);
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                resp.NewRecordIDs.Clear();
                                return resp;
                            }
                            //List<BaseTSo> tsoDetails = new List<BaseTSo>();
                            IList<ITSO> tsoDetails = coreFactory.ListITSO();
                            foreach (int tsoId in readTSoIds.ListOfIDs)
                            {
                                IDcTsoId tsoIdReq = coreFactory.DcTsoId(request_obj.coreProj);
                                tsoIdReq.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                                tsoIdReq.orgId = request_obj.orgId;
                                tsoIdReq.tsoId = tsoId;

                                IDcrTSO tsoData = coreSc.Read_TSo(tsoIdReq, validation, utils, coreSc, coreDb,coreFactory);
                                if (tsoData.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(tsoData);
                                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                    resp.NewRecordIDs.Clear();
                                    return resp;
                                }
                                if (tsoData.repeatId == -1 && tsoData.exceptionId == request_obj.exceptionId)
                                {
                                    //skip the exception obj itself as that will be part of the new repeat ex map
                                }
                                else
                                {
                                    tsoDetails.Add(new BaseTSo(tsoData));
                                }
                            }

                            List<IInstantStartStop> conflicts = utils.GetConflictingTimePeriods(utils.CONVERT_ITSOListToInstantList(tsoDetails), generatedRepeatTimePeriods);
                            //if (conflicts[0].func_msg != ENUM_Cmd_Status.ok)
                            //{
                            //    resp.StatusAndMessage_CopyFrom(conflicts[0]);
                            //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            //    resp.NewRecordIDs.Clear();
                            //    return resp;
                            //}

                            //if (conflicts.TimePeriods.Count != 0)
                                if (conflicts.Count != 0)
                            {
                                resp.SetResponsePermissionDenied();
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                resp.NewRecordIDs.Clear();
                                return resp;
                            }
                        }
                    }

                    foreach (int repeatId in request_obj.repeatIds)
                    {
                        IDcMapRepeatException createRepeatExceptionMap = coreFactory.DcMapRepeatException(request_obj.coreProj);
                        createRepeatExceptionMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        //createRepeatExceptionMap.creatorId = request_obj.creatorId;
                        createRepeatExceptionMap.exceptionId = request_obj.exceptionId;
                        createRepeatExceptionMap.orgId = request_obj.orgId;
                        createRepeatExceptionMap.repeatId = repeatId;
                        INewRecordId repeatedExceptionRepeatMap = coreFactory.NewRecordId();
                        if (coreDb.Create_Exception_Repeat_Map(request_obj.coreProj, request_obj, createRepeatExceptionMap, repeatedExceptionRepeatMap) != ENUM_DB_Status.DB_SUCCESS)
                        {
                            resp.SetResponseServerError();
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        //generate the new exception events for the configured system boundary
                        foreach (IInstantStartStop timeRange in repeatTimePeriods[repeatId])
                        {
                            IDcCreateTSO createTso = coreFactory.DcCreateTSO(request_obj.coreProj);
                            createTso.appointmentId = 0;
                            createTso.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            createTso.dateOfGeneration = DateTime.Now.ToUniversalTime().ISO8601Str();
                            createTso.durationMilliseconds = (long)timeRange.stop.Minus(timeRange.start).ToTimeSpan().TotalMilliseconds;
                            createTso.exceptionId = request_obj.exceptionId;
                            createTso.repeatId = repeatId;
                            createTso.resourceIdList = listOfResourcesMappedToException.ListOfIDs;
                            createTso.start = timeRange.start.ToDateTimeUtc().ISO8601Str();
                            createTso.end = timeRange.stop.ToDateTimeUtc().ISO8601Str();
                            createTso.orgId = request_obj.orgId;
                            //DCR_Added createdTso = SC_TSO.Create_TimePeriod(createTso);
                            //List<ITimeStartEnd> timeStartEnd = new List<ITimeStartEnd>();
                            IDCR_Added createdTso = coreSc.Create_TimePeriod(createTso, validation , utils,  coreSc, coreDb,coreFactory);
                            if (createdTso.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(createdTso);
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                return resp;
                            }
                        }
                        resp.NewRecordIDs.Add(repeatedExceptionRepeatMap.NewRecordID);
                    }
                    resp.Result = ENUM_Cmd_Add_Result.Added;
                    resp.SetResponseOk();
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
            }

            return resp;
        }

        public IDCR_Added Create_Org_Exception_Repeat_Map(IDcMapRepeatException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
        
            DCR_Added resp = new DCR_Added();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcMapRepeatException)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_createOrgExceptionRepeatMap))
                {
                    //TODO: check the return values of the reads
                    #region read the exception data
                    IDcExceptionID dcExceptionID = coreFactory.DcExceptionID(request_obj.coreProj);
                    dcExceptionID.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcExceptionID.exceptionId = request_obj.exceptionId;
                    dcExceptionID.orgId = request_obj.orgId;

                    IDcrException exceptionDetails = coreSc.Read_Org_Exception_By_Exception_ID(dcExceptionID,  validation, utils, coreSc, coreDb,coreFactory);
                    #endregion
                    #region read the repeat data
                    IDcRepeatId dcRepeatId = coreFactory.DcRepeatId(request_obj.coreProj);
                    dcRepeatId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcRepeatId.orgId = request_obj.orgId;
                    dcRepeatId.repeatId = request_obj.repeatId;


                    IDcrRepeat repeatDetails = coreSc.Read_Repeat(dcRepeatId, validation, utils,  coreSc, coreDb,coreFactory);
                    if (repeatDetails.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(repeatDetails);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region read the exception repeat mappings

                    IDcrIdList exceptionsRepeatAlreadyMapd = coreSc.Read_All_Org_Exception_Repeat_Mappings_By_Exception_ID(dcExceptionID,  validation, utils, coreSc, coreDb,coreFactory);
                    if (exceptionsRepeatAlreadyMapd.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionsRepeatAlreadyMapd);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    if (exceptionsRepeatAlreadyMapd.ListOfIDs.Contains(dcRepeatId.repeatId))
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Already_Added;
                        resp.SetResponsePermissionDenied();
                        return resp;
                    }
                    #endregion
                    //the repeat rule shouldnt really be allowed to have a start date for before the exception
                    /*if (DateTime.Parse(repeatDetails.start) < DateTime.Parse(exceptionDetails.stop))
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.NewRecordID = -1;
                        resp.SetResponsePermissionDenied();
                        return resp;
                    }*/
                    #region create a tmp copy of the repeated events for the system configured boundary
                    ITimeStartEnd eventStartStop = coreFactory.TimeStartEnd();
                    eventStartStop.start = DateTime.Parse(exceptionDetails.start).ToUniversalTime().ISO8601Str();
                    eventStartStop.end = DateTime.Parse(exceptionDetails.end).ToUniversalTime().ISO8601Str();

                    List<IInstantStartStop> generatedRepeatTimePeriods = utils.GenerateRepeatTimePeriods(request_obj.coreProj, coreSc, eventStartStop, repeatDetails, exceptionDetails.timeZoneIANA, true, coreDb, coreFactory);
                    //TODO: fix bug here due things going over the year boundary from today.....................
                    //if (generatedRepeatTimePeriods.func_status != ENUM_Cmd_Status.ok)
                    //{
                    //    resp.StatusAndMessage_CopyFrom(generatedRepeatTimePeriods);
                    //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    //    return resp;
                    //}
                    #endregion
                    #region check to see if the exception is linked to any resources

                    IDcrIdList listOfResourcesMappedToException = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(dcExceptionID,  validation, utils, coreSc, coreDb,coreFactory);
                    if (listOfResourcesMappedToException.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(listOfResourcesMappedToException);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region check to see if the exception is linked to any calendars

                    IDcrIdList listOfCalendarsMappedToException = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(dcExceptionID,  validation, utils, coreSc, coreDb,coreFactory);
                    if (listOfCalendarsMappedToException.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(listOfCalendarsMappedToException);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region loop all the resources and check for conflicts in the time period we are concerned with
                    foreach (int resourceId in listOfResourcesMappedToException.ListOfIDs)
                    {
                        IDCResourceTimeRange dcResourceTimeRange = coreFactory.DCResourceTimeRange(request_obj.coreProj);
                        dcResourceTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcResourceTimeRange.orgId = request_obj.orgId;
                        dcResourceTimeRange.resourceId = resourceId;
                        dcResourceTimeRange.start = DateTime.Parse(exceptionDetails.start).OrijDTStr();
                        DateTime tmp = DateTime.Parse(exceptionDetails.start).AddDays(GeneralConfig.MAX_GENERATE_FUTURE_IN_DAYS).ToUniversalTime();
                        tmp = new DateTime(tmp.Year, tmp.Month, tmp.Day, 23, 59, 59).ToUniversalTime();
                        dcResourceTimeRange.end = tmp.ISO8601Str();
                        //resourceTimeRange.durationMilliseconds = (long)(DateTime.Parse(resourceTimeRange.end) - DateTime.Parse(resourceTimeRange.start)).TotalMilliseconds;

                        //test to see if any of the resources have tso which conflict with the new exceptions

                        IDcrIdList readTSoIds = coreSc.Read_Resource_TSOs_By_Resource_ID_TimeRange(dcResourceTimeRange,  validation, utils, coreSc, coreDb,coreFactory);
                        if (readTSoIds.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(readTSoIds);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        //List<BaseTSo> tsoDetails = new List<BaseTSo>();
                        IList<ITSO> tsoDetails = coreFactory.ListITSO();
                        foreach (int tsoId in readTSoIds.ListOfIDs)
                        {
                            IDcTsoId dcTsoId = coreFactory.DcTsoId(request_obj.coreProj);
                            dcTsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcTsoId.orgId = request_obj.orgId;
                            dcTsoId.tsoId = tsoId;

                            IDcrTSO tsoData = coreSc.Read_TSo(dcTsoId,  validation, utils, coreSc, coreDb,coreFactory);


                            if (tsoData.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(tsoData);
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                return resp;
                            }
                            if ((tsoData.repeatId == -1 || tsoData.repeatId == 0) && tsoData.exceptionId == request_obj.exceptionId)
                            {
                                //skip the exception obj itself as that will be part of the new repeat ex map
                            }
                            else
                            {
                                tsoDetails.Add(new BaseTSo(tsoData));
                            }
                        }

                        //DCR_TimePeriod_List conflicts = Utils.GetConflictingTimePeriods(Utils.CONVERT_BaseTSOToInstantList(tsoDetails), generatedRepeatTimePeriods.TimePeriods);
                        List<IInstantStartStop> conflicts = utils.GetConflictingTimePeriods(utils.CONVERT_ITSOListToInstantList(tsoDetails), generatedRepeatTimePeriods);
                        //if (conflicts.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(conflicts);
                        //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        //    return resp;
                        //}

                        //if (conflicts.TimePeriods.Count != 0)
                        if (conflicts.Count != 0)
                        {
                            resp.SetResponsePermissionDenied();
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }

                    }
                    #endregion
                    #region loop all the calendars and check for conflicts in the time period
                    foreach (int calendarId in listOfCalendarsMappedToException.ListOfIDs)
                    {
                        IDcCalendarTimeRange dcCalendarTimeRange = coreFactory.DcCalendarTimeRange(request_obj.coreProj);
                        dcCalendarTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcCalendarTimeRange.orgId = request_obj.orgId;
                        dcCalendarTimeRange.calendarId = calendarId;
                        dcCalendarTimeRange.start = exceptionDetails.start;
                        dcCalendarTimeRange.end = Instant.FromDateTimeUtc(DateTime.Now.ToUniversalTime()).Plus(Duration.FromStandardDays(GeneralConfig.MAX_GENERATE_FUTURE_IN_DAYS)).ToDateTimeUtc().ISO8601Str();
                        //calendarTimeRange.durationMilliseconds = (long)(DateTime.Parse(calendarTimeRange.end) - DateTime.Parse(calendarTimeRange.start)).TotalMilliseconds;

                        //test to see if any of the calendars have tso which conflict with the new exceptions
                        IDcrIdList readTSoIds = coreSc.Read_Org_Calendar_TSOs_By_Calendar_ID_And_TimeRange(dcCalendarTimeRange, validation, utils, coreSc, coreDb,coreFactory);

                        if (readTSoIds.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(readTSoIds);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                        //List<BaseTSo> tsoDetails = new List<BaseTSo>();
                        IList<ITSO> tsoDetails = coreFactory.ListITSO();
                        foreach (int tsoId in readTSoIds.ListOfIDs)
                        {
                            IDcTsoId dcTsoId = coreFactory.DcTsoId(request_obj.coreProj);
                            dcTsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcTsoId.orgId = request_obj.orgId;
                            dcTsoId.tsoId = tsoId;

                            IDcrTSO tsoData = coreSc.Read_TSo(dcTsoId,  validation, utils, coreSc, coreDb,coreFactory);
                            if (tsoData.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(tsoData);
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                return resp;
                            }
                            if (tsoData.repeatId == -1 && tsoData.exceptionId == request_obj.exceptionId)
                            {
                                //skip the exception obj itself as that will be part of the new repeat ex map
                            }
                            else
                            {
                                tsoDetails.Add(new BaseTSo(tsoData));
                            }
                        }

                        List<IInstantStartStop> conflicts = utils.GetConflictingTimePeriods(utils.CONVERT_ITSOListToInstantList(tsoDetails), generatedRepeatTimePeriods);
                        //if (conflicts.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(conflicts);
                        //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        //    return resp;
                        //}

                        if (conflicts.Count != 0)
                        {
                            resp.SetResponsePermissionDenied();
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }

                    }
                    #endregion
                    //if no conflicts occur proceed to map the repeat rule with the exception
                    //IDcMapRepeatException createRepeatExceptionMap = coreFactory.DcMapRepeatException(request_obj.coreProj);
                    //createRepeatExceptionMap.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    ////createRepeatExceptionMap.creatorId = request_obj.creatorId;
                    //createRepeatExceptionMap.exceptionId = request_obj.exceptionId;
                    //createRepeatExceptionMap.orgId = request_obj.orgId;
                    //createRepeatExceptionMap.repeatId = request_obj.repeatId;
                    INewRecordId repeatedExceptionRepeatMap = coreFactory.NewRecordId();
                    if (coreDb.Create_Exception_Repeat_Map(request_obj.coreProj, request_obj, request_obj, repeatedExceptionRepeatMap) != ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }

                    //generate the new exception events for the configured system boundary
                    IDC_Create_Time_Scale_Objs createTimeScaleObjs = coreFactory.DCCreateTimeScaleObjs(request_obj.coreProj);
                    createTimeScaleObjs.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createTimeScaleObjs.orgId = request_obj.orgId;
                    //foreach (BaseInstantStartStop timeRange in generatedRepeatTimePeriods.TimePeriods)
                    foreach (IInstantStartStop timeRange in generatedRepeatTimePeriods)
                    {
                        ITsoOptions createTso = coreFactory.TsoOptions();
                        createTso.appointmentId = 0;
                        createTso.dateOfGeneration = DateTime.Now.ToUniversalTime().ISO8601Str();
                        createTso.durationMilliseconds = (long)timeRange.stop.Minus(timeRange.start).ToTimeSpan().TotalMilliseconds;
                        createTso.exceptionId = request_obj.exceptionId;
                        createTso.repeatId = request_obj.repeatId;
                        createTso.start = timeRange.start.ToDateTimeUtc().ISO8601Str();
                        createTso.end = timeRange.stop.ToDateTimeUtc().ISO8601Str();
                        createTso.orgId = request_obj.orgId;
                        createTimeScaleObjs.listOfTSOOptions.Add(createTso);
                    }
                    createTimeScaleObjs.resourceIdList = listOfResourcesMappedToException.ListOfIDs;
                    createTimeScaleObjs.calendarIdList = listOfCalendarsMappedToException.ListOfIDs;
                    //DCR_Added_List createdTSOS = SC_TSO.Create_Org_TimeScaleObjects(createTimeScaleObjs);
                    IList<ITimeStartEnd> listTimeStartEnd = coreFactory.ListInstantStartEnd();

                    IDcTsos dcTsos = coreFactory.DcTsos(request_obj.coreProj);

                    IDcrAddedList createdTSOS = coreSc.Create_Org_TimeScaleObjects(dcTsos, validation, utils,  coreSc, coreDb, coreFactory);
                    if (createdTSOS.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(createdTSOS);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }

                    resp.Result = ENUM_Cmd_Add_Result.Added;
                    resp.NewRecordID = repeatedExceptionRepeatMap.NewRecordID;
                    resp.SetResponseOk();

                }
                else
                {
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDCR_Added Create_Org_Exception_Resource_Mapping(IDcResourceException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Added resp = new DCR_Added();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcResourceException)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_createOrgResourceExceptionMappingByExceptionID))
                {
                    #region read the exception details
                    IDcExceptionID dcExceptionID = coreFactory.DcExceptionID(request_obj.coreProj);
                    dcExceptionID.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcExceptionID.orgId = request_obj.orgId;
                    dcExceptionID.exceptionId = request_obj.exceptionId;

                    IDcrException exceptionData = coreSc.Read_Org_Exception_By_Exception_ID(dcExceptionID,  validation, utils, coreSc, coreDb,coreFactory);
                    if (exceptionData.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionData);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region read all the resources mapped to the exception

                    IDcrIdList resourcesAlreadyMapped = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb,coreFactory);
                    if (resourcesAlreadyMapped.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourcesAlreadyMapped);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region check to see if the resource is already mapped to the exception
                    if (resourcesAlreadyMapped.ListOfIDs.Contains(request_obj.resourceId))
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Already_Added;
                        resp.SetResponseOk();
                        return resp;
                    }
                    #endregion
                    #region read the resource which will be mapped
                    IDcOrgResourceId dcOrgResourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);
                    dcOrgResourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcOrgResourceId.orgId = request_obj.orgId;
                    dcOrgResourceId.resourceId = request_obj.resourceId;
                    IDcrResourceComplete resourceDetails = coreSc.Read_Resource_By_ID(dcOrgResourceId,  validation, utils, coreSc, coreDb,coreFactory);
                    if (resourceDetails.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourceDetails);
                        resp.NewRecordID = -1;
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion
                    #region read TSO's already generated and mapped to the exception

                    IDcrTsoList exceptionTSOs = coreSc.Read_All_TimePeriods_For_Exception(dcExceptionID, validation, utils, coreSc, coreDb,coreFactory);
                    if (exceptionTSOs.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionTSOs);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }

                    IList<IInstantStartStop> currentExceptionTimePeriods = coreFactory.ListInstantStartStop();
                    foreach (ITSO tso in exceptionTSOs.timeScaleList)
                    {
                        //BaseInstantStartStop tr = new BaseInstantStartStop();

                        IInstantStartStop tr = coreFactory.InstantStartStop();
                        tr.start = InstantPattern.ExtendedIsoPattern.Parse(tso.start).Value;
                        tr.stop = InstantPattern.ExtendedIsoPattern.Parse(tso.end).Value;
                        currentExceptionTimePeriods.Add(tr);
                    }
                    #endregion
                    #region if the resource to be mapped allows overlapping then just create the TSO objects
                    if (resourceDetails.allowsOverlaps == Enum_SYS_BookingOverlap.OverLappingAllowed)
                    {
                        
                        //update all the TSO's                           
                        IDcResourceTSO dcResourceTSO = coreFactory.DcResourceTSO(request_obj.coreProj);
                        dcResourceTSO.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcResourceTSO.orgId = request_obj.orgId;
                        dcResourceTSO.resourceId = request_obj.resourceId;
                        foreach (ITSO exceptionTSO in exceptionTSOs.timeScaleList)
                        {
                            dcResourceTSO.tsoId = exceptionTSO.tsoId;

                            IDCR_Added addedResTSOMap = coreSc.Create_TimePeriod_Resource_Map(dcResourceTSO, validation, utils, coreSc, coreDb,coreFactory);
                            if (addedResTSOMap.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(addedResTSOMap);
                                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                                return resp;
                            }
                        }

                        INewRecordId newMapId = coreFactory.NewRecordId();

                        if (coreDb.Create_Resource_Exception_Mapping(request_obj.coreProj, dcResourceTSO, dcExceptionID, newMapId) == ENUM_DB_Status.DB_SUCCESS)
                        {
                            resp.Result = ENUM_Cmd_Add_Result.Added;
                            resp.NewRecordID = newMapId.NewRecordID;
                            resp.SetResponseOk();
                            return resp;
                        }
                        else
                        {
                            resp.SetResponseServerError();
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                    }
                    #endregion
                    #region do this if the resource does not allow overlapping


                    #region check if they conflict with the old TSO objects
                    IDCResourceTimeRange dcResourceTimeRange = coreFactory.DCResourceTimeRange(request_obj.coreProj);
                    dcResourceTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcResourceTimeRange.orgId = request_obj.orgId;
                    dcResourceTimeRange.resourceId = request_obj.resourceId;
                    dcResourceTimeRange.start = exceptionData.start;
                    dcResourceTimeRange.end = DateTime.Now.AddDays(GeneralConfig.MAX_GENERATE_FUTURE_IN_DAYS).ToUniversalTime().ISO8601Str();

                    IDcrTsoList resourceTSOs = coreSc.Read_TimePeriods_For_Resource_Between_DateTime(dcResourceTimeRange,  validation, utils, coreSc, coreDb,coreFactory);
                    //List<BaseInstantStartStop> alreadyAllocatedTimeRanges = new List<BaseInstantStartStop>();
                    IList<IInstantStartStop> alreadyAllocatedTimeRanges = coreFactory.ListInstantStartStop();
                    if (resourceTSOs.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourceTSOs);
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    IInstantStartStop resourceTimeRanges = coreFactory.InstantStartStop();
                    foreach (ITSO tsoDetails in resourceTSOs.timeScaleList)
                    {
                        IInstantStartStop tr = coreFactory.InstantStartStop();
                        tr.start = InstantPattern.ExtendedIsoPattern.Parse(tsoDetails.start).Value;
                        tr.stop = InstantPattern.ExtendedIsoPattern.Parse(tsoDetails.end).Value;
                        alreadyAllocatedTimeRanges.Add(tr);
                    }
                    #endregion

                    #region if they conflict then disallow the add otherwise add the map to the existing exception TSOS
                    //DCR_TimePeriod_List conflicts = Utils.GetConflictingTimePeriods(alreadyAllocatedTimeRanges, currentExceptionTimePeriods);
                    List<IInstantStartStop> conflicts = utils.GetConflictingTimePeriods(resourceTimeRanges, alreadyAllocatedTimeRanges);
                    //if (conflicts.func_status != ENUM_Cmd_Status.ok)
                    //{
                    //    resp.StatusAndMessage_CopyFrom(conflicts);
                    //    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    //    return resp;
                    //}
                    if (conflicts.Count != 0)
                    {
                        //there are conflicts so cannot add
                        resp.SetResponsePermissionDenied();
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        return resp;
                    }
                    #endregion

                    #region create the link for the exception + resource in the db
                    //foreach (BaseTSo tso in exceptionTSOs.timeScaleList)
                    foreach (ITSO tso in exceptionTSOs.timeScaleList)
                    {
                        IDcResourceTSO dcResourceTSO = coreFactory.DcResourceTSO(request_obj.coreProj);
                        dcResourceTSO.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcResourceTSO.orgId = request_obj.orgId;
                        dcResourceTSO.resourceId = request_obj.resourceId;
                        dcResourceTSO.tsoId = tso.tsoId;

                        IDCR_Added createdTsoResMap = coreSc.Create_TimePeriod_Resource_Map(dcResourceTSO,  validation, utils, coreSc, coreDb,coreFactory);
                        if (createdTsoResMap.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(createdTsoResMap);
                            resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                            return resp;
                        }
                    }
                    INewRecordId newMappingID = coreFactory.NewRecordId();
                    
                    if (coreDb.Create_Resource_Exception_Mapping(request_obj.coreProj, request_obj, dcExceptionID, newMappingID) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.NewRecordID = newMappingID.NewRecordID;
                        resp.Result = ENUM_Cmd_Add_Result.Added;
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                        resp.SetResponseServerError();
                    }
                    #endregion
                    #endregion
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Add_Result.Not_Added;
                return resp;
            }
            return resp;

        }

        public IDCR_Delete Delete_All_Exception_TSO_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            
                DCR_Delete resp = new DCR_Delete();


                if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
                {
                    if (request_obj.cmd_user_id == GeneralConfig.SYSTEM_WILDCARD_INT)
                    {

                        IDcrIdList exceptionTsos = coreSc.Read_All_Org_Exception_TSoIds_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                        if (exceptionTsos.func_status == ENUM_Cmd_Status.ok)
                        {
                            foreach (int tsoId in exceptionTsos.ListOfIDs)
                            {
                            IDcTsoId deleteTso = coreFactory.DcTsoId(request_obj.coreProj);
                            deleteTso.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            deleteTso.orgId = request_obj.orgId;
                            deleteTso.tsoId = tsoId;

                                IDCR_Delete delTso = coreSc.Delete_TimePeriod(deleteTso, coreSc, coreDb);
                                if (delTso.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(delTso);
                                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                    return resp;
                                }
                            }
                            resp.SetResponseOk();
                            resp.Result = ENUM_Cmd_Delete_State.Deleted;
                        }
                        else
                        {
                            resp.StatusAndMessage_CopyFrom(exceptionTsos);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        }
                    }
                    else
                    {
                        resp.SetResponsePermissionDenied();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    }
                }
                else
                {
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                }
                return resp;
            }

        public IDCR_Delete Delete_All_Org_Exception_Calendar_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Delete resp = new DCR_Delete();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteAllExceptionCalendarMappingsByExceptionID))
                {
                    IDcrIdList exceptionCalendarList = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb,coreFactory);
                    if (exceptionCalendarList.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionCalendarList);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    foreach (int calendarId in exceptionCalendarList.ListOfIDs)
                    {
                        IDcCalendarId dcCalendarId = coreFactory.DcCalendarId(request_obj.coreProj);
                        dcCalendarId.calendarId = calendarId;
                        dcCalendarId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcCalendarId.orgId = request_obj.orgId;
                        //DCR_Id_List calendarResourceMappings = SC_Org_Calendars.Read_All_Org_Calendar_Resource_Mappings_By_Calendar_ID(calendarIdRequest);
                        IDcrIdList calendarResourceMappings = coreSc.Read_All_Org_Calendar_Resource_Mappings_By_Calendar_ID(dcCalendarId, validation, utils, coreSc, coreDb, coreFactory);
                        if (calendarResourceMappings.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(calendarResourceMappings);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                        //DCR_Org_TSO_List calendarTSOs = SC_TSO.Read_All_TimePeriods_For_Calendar(calendarIdRequest);
                        IDcrTsoList calendarTSOs = coreSc.Read_All_TimePeriods_For_Calendar(dcCalendarId,  validation, utils, coreSc, coreDb, coreFactory);
                        if (calendarTSOs.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(calendarTSOs);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                        foreach (ITSO tsoDetails in calendarTSOs.timeScaleList)
                        {
                            if (tsoDetails.exceptionId == request_obj.exceptionId)
                            {
                                foreach (int resId in calendarResourceMappings.ListOfIDs)
                                {
                                    IDcTSOResourceId tsoResId = coreFactory.DcTSOResourceId(request_obj.coreProj);
                                    tsoResId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                                    tsoResId.orgId = request_obj.orgId;
                                    tsoResId.resourceId = resId;
                                    tsoResId.tsoId = tsoDetails.tsoId;
                                    IDCR_Delete deletedTsoRes = coreSc.Delete_TimePeriod_Resource_Map(tsoResId, coreDb);
                                    if (deletedTsoRes.func_status != ENUM_Cmd_Status.ok)
                                    {
                                        resp.StatusAndMessage_CopyFrom(deletedTsoRes);
                                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                        return resp;
                                    }
                                }
                                IDcTSOCalendarId tsoCalendarId = coreFactory.DcTSOCalendarId(request_obj.coreProj);
                                tsoCalendarId.calendarId = calendarId;
                                tsoCalendarId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                                tsoCalendarId.orgId = request_obj.orgId;
                                tsoCalendarId.tsoId = tsoDetails.tsoId;

                                IDCR_Delete deletedTSO = coreSc.Delete_TimePeriod_Calendar_Map(tsoCalendarId, coreDb);
                                if (deletedTSO.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(deletedTSO);
                                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                    return resp;
                                }
                            }
                        }
                    }

                    if (coreDb.Delete_All_Exception_Calendar_Mappings(request_obj.coreProj, request_obj) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.SetResponseOk();
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                return resp;
            }
            return resp;
        }

        public IDCR_Delete Delete_All_Org_Exception_Repeat_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
         
            DCR_Delete resp = new DCR_Delete();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteAllExceptionRepeatMappingsByExceptionID))
                {
                    IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                    exceptionId.exceptionId = request_obj.exceptionId;
                    exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    exceptionId.orgId = request_obj.orgId;

                    IDcrIdList repeatMappings = coreSc.Read_All_Org_Exception_Repeat_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb,coreFactory);
                    if (repeatMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(repeatMappings);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    foreach (int repeatId in repeatMappings.ListOfIDs)
                    {
                        //we are basically removing the link from the time periods from the exceptions
                        IDcExceptionRepeatId exceptionRepeatId = coreFactory.DcExceptionRepeatId(request_obj.coreProj);
                        exceptionRepeatId.exceptionId = request_obj.exceptionId;
                        exceptionRepeatId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        exceptionRepeatId.orgId = request_obj.orgId;
                        exceptionRepeatId.repeatId = repeatId;

                        IDcrIdList tsoIds = coreSc.Read_All_Org_Exception_TSoIds_Filter_By_Repeat_ID(exceptionRepeatId,  validation, utils, coreSc, coreDb,coreFactory);
                        if (tsoIds.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(tsoIds);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                        foreach (int tsoId in tsoIds.ListOfIDs)
                        {
                            IDcTsoId dcTsoId = coreFactory.DcTsoId(request_obj.coreProj);
                            dcTsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcTsoId.orgId = request_obj.orgId;
                            dcTsoId.tsoId = tsoId;

                            IDCR_Delete deleteTSO = coreSc.Delete_TimePeriod(dcTsoId, coreSc, coreDb);
                            if (deleteTSO.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(deleteTSO);
                                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                return resp;
                            }
                        }
                    }
                    if (coreDb.Delete_All_Exception_Repeat_Mappings(request_obj.coreProj, request_obj) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.SetResponseOk();
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                return resp;
            }
            return resp;
        }

       public IDCR_Delete Delete_All_Org_Exception_Resource_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            
                DCR_Delete resp = new DCR_Delete();

                if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcExceptionID)))
                {
                    if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteAllOrgAppointmentResourceMappingsByAppointmentID))
                    {
                    IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                    //    exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //    exceptionId.exceptionId = request_obj.exceptionId;
                    //    exceptionId.orgId = request_obj.orgId;

                        #region read all the exception tso mappings

                        IDcrTsoList exceptionTimePeriods = coreSc.Read_All_TimePeriods_For_Exception(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                        if (exceptionTimePeriods.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(exceptionTimePeriods);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                        #endregion
                        #region read all the exception resources

                        IDcrIdList resourceList = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                        if (resourceList.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(resourceList);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                        #endregion
                        foreach (int resId in resourceList.ListOfIDs)
                        {
                        #region read all the resource tso maps
                        IDcOrgResourceId resourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);;
                            resourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            resourceId.orgId = request_obj.orgId;
                            resourceId.resourceId = resId;
                            //DCR_Org_TSO_List resourceTsoList = SC_TSO.Read_All_TimePeriods_For_Resource(resourceId);
                            IDcrTsoList resourceTsoList = coreSc.Read_All_TimePeriods_For_Resource(resourceId,  validation, utils, coreSc, coreDb, coreFactory);
                            if (resourceTsoList.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(resourceTsoList);
                                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                return resp;
                            }
                            #endregion
                            #region loop both of mappings and look for matches
                            var tsoFilteredByExId = exceptionTimePeriods.timeScaleList.Select(emp => emp.exceptionId).ToHashSet();
                            //List<BaseTSo> matchedTsos = resourceTsoList.timeScaleList.Where(product => tsoFilteredByExId.Contains(product.exceptionId)).ToList();
                            List<ITSO> matchedTsos = resourceTsoList.timeScaleList.Where(product => tsoFilteredByExId.Contains(product.exceptionId)).ToList();
                            #endregion
                            #region delete all of the matches
                            //foreach (BaseTSo tsoToDelete in matchedTsos)
                            foreach (ITSO tsoToDelete in matchedTsos)
                            {
                            IDcTSOResourceId tsoId = coreFactory.DcTSOResourceId(request_obj.coreProj);
                                tsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                                tsoId.orgId = tsoToDelete.orgId;
                                tsoId.tsoId = tsoToDelete.tsoId;
                                tsoId.resourceId = resId;
                                //DCR_Delete deleteMap = SC_TSO.Delete_TimePeriod_Resource_Map(tsoId);
                                IDCR_Delete deleteMap = coreSc.Delete_TimePeriod_Resource_Map(tsoId, coreDb);
                                if (deleteMap.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(deleteMap);
                                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                    return resp;
                                }
                            }
                        #endregion
                        #region delete the final map of the resource and exception
                        IDcResourceException resourceExId = coreFactory.DcResourceException(request_obj.coreProj);
                            resourceExId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            resourceExId.exceptionId = request_obj.exceptionId;
                            resourceExId.orgId = request_obj.orgId;
                            resourceExId.resourceId = resId;
                            if (coreDb.Delete_Exception_Resource_Mapping(request_obj.coreProj, request_obj, resourceExId) == ENUM_DB_Status.DB_SUCCESS)
                            {
                                resp.Result = ENUM_Cmd_Delete_State.Deleted;
                                resp.SetResponseOk();
                            }
                            else
                            {
                                resp.SetResponseServerError();
                                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                return resp;
                            }
                            #endregion
                        }
                        resp.SetResponseOk();
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                    }
                    else
                    {
                        resp.SetResponsePermissionDenied();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
                return resp;
            }

        public IDCR_Delete Delete_All_Org_Resource_Exception_Mappings_By_Resource_ID(IDcOrgResourceId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
          
            DCR_Delete resp = new DCR_Delete();
            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcOrgResourceId)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteAllResourceExceptionMappingsByResourceID))
                {
                    if (coreDb.Delete_All_Resource_Exception_Mappings(request_obj.coreProj, request_obj, request_obj) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.SetResponseOk();
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                return resp;
            }
            return resp;
        }

        public IDCR_Delete Delete_Org_Exception_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {

            DCR_Delete resp = new DCR_Delete();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteOrgExceptionByExceptionID))
                {
                    //DC_Org_Exception_Id exceptionId = new DC_Org_Exception_Id(request_obj.coreProj);
                    //exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //exceptionId.exceptionId = request_obj.exceptionId;
                    //exceptionId.orgId = request_obj.orgId;
                    //remove the all tso objects

                    IDCR_Delete deleteExTsoMaps = coreSc.Delete_All_Exception_TSO_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (deleteExTsoMaps.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deleteExTsoMaps);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    //remove the link to the repeats
                    IDCR_Delete deletedExRepMaps = coreSc.Delete_All_Org_Exception_Repeat_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (deletedExRepMaps.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deletedExRepMaps);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    //remove the link to the resources
                    IDCR_Delete deletedExResMaps = coreSc.Delete_All_Org_Exception_Resource_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (deletedExResMaps.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deletedExResMaps);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }

                    IDCR_Delete deletedExCalMaps = coreSc.Delete_All_Org_Exception_Calendar_Mappings_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (deletedExCalMaps.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deletedExCalMaps);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    //remove the exception details
                    if (coreDb.Delete_Exception(request_obj.coreProj, request_obj.exceptionId) != ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        resp.SetResponseServerError();
                        return resp;
                    }
                    else
                    {
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                        resp.SetResponseOk();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                return resp;
            }
        }

        public IDCR_Delete Delete_Org_Exception_Calendar_Mapping(IDcCalendarExceptionId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            
                DCR_Delete resp = new DCR_Delete();
                if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcCalendarExceptionId)))
                {
                    if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteExceptionCalendarMapping))
                    {
                    //TODO: this function can be optimized
                    IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                        exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        exceptionId.exceptionId = request_obj.exceptionId;
                        exceptionId.orgId = request_obj.orgId;
                    IDcCalendarTimeRange calendarTimeRange = coreFactory.DcCalendarTimeRange(request_obj.coreProj);
                        calendarTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        calendarTimeRange.orgId = request_obj.orgId;
                        calendarTimeRange.calendarId = request_obj.calendarId;
                        calendarTimeRange.start = GeneralConfig.DEFAULT_SYSTEM_MIN_DATE;
                        calendarTimeRange.end = GeneralConfig.DEFAULT_SYSTEM_MAX_DATE;

                        IDcrIdList calendarTSOs = coreSc.Read_Org_Calendar_TSOs_By_Calendar_ID_And_TimeRange(calendarTimeRange, validation, utils, coreSc, coreDb, coreFactory);
                        if (calendarTSOs.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(calendarTSOs);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }


                        IDcrIdList exceptionIds = coreSc.Read_All_Org_Exception_TSoIds_By_Exception_ID(exceptionId,  validation, utils, coreSc, coreDb, coreFactory);
                        if (exceptionIds.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(exceptionIds);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }

                        List<int> matchedTSOids = calendarTSOs.ListOfIDs.Intersect(exceptionIds.ListOfIDs).ToList();
                        if (matchedTSOids.Count >= 0)
                        {
                        //there are tsos to be removed
                        IDcTsoId deleteTso = coreFactory.DcTsoId(request_obj.coreProj);
                            deleteTso.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            deleteTso.orgId = request_obj.orgId;
                            foreach (int tsoId in matchedTSOids)
                            {
                                deleteTso.tsoId = tsoId;
                                IDCR_Delete deletedTso = coreSc.Delete_TimePeriod(deleteTso, coreSc, coreDb);
                                if (deletedTso.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(deletedTso);
                                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                                    return resp;
                                }
                            }
                        }
                        if (coreDb.Delete_Exception_Calendar_Mapping(request_obj.coreProj, request_obj, exceptionId) == ENUM_DB_Status.DB_SUCCESS)
                        {
                            resp.Result = ENUM_Cmd_Delete_State.Deleted;
                            resp.SetResponseOk();
                            return resp;
                        }
                        else
                        {
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            resp.SetResponseServerError();
                            return resp;
                        }
                    }
                    else
                    {
                        resp.SetResponsePermissionDenied();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponseInvalidParameter();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }

        public IDCR_Delete Delete_Org_Exception_Repeat_Map(IDcExceptionRepeatId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Delete resp = new DCR_Delete();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcExceptionRepeatId)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteOrgExceptionRepeatMap))
                {
                    //IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                    //exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //exceptionId.exceptionId = request_obj.exceptionId;
                    //exceptionId.orgId = request_obj.orgId;

                    IDcrException exceptionDetails = coreSc.Read_Org_Exception_By_Exception_ID(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (exceptionDetails.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionDetails);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    //IDcExceptionRepeatId exRepId = coreFactory.DcExceptionRepeatId(request_obj.coreProj);
                    //exRepId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //exRepId.exceptionId = request_obj.exceptionId;
                    //exRepId.orgId = request_obj.orgId;
                    //exRepId.repeatId = request_obj.repeatId;

                    IDcrIdList exRepeatTSOIds = coreSc.Read_All_Org_Exception_TSoIds_Filter_By_Repeat_ID(request_obj,validation, utils,  coreSc, coreDb, coreFactory);
                    if (exRepeatTSOIds.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exRepeatTSOIds);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    foreach (int mappedTso in exRepeatTSOIds.ListOfIDs)
                    {
                        IDcTsoId deleteMe = coreFactory.DcTsoId(request_obj.coreProj);
                        deleteMe.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        deleteMe.orgId = request_obj.orgId;
                        deleteMe.tsoId = mappedTso;

                        IDCR_Delete deletedTso = coreSc.Delete_TimePeriod(deleteMe, coreSc, coreDb);
                        if (deletedTso.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(deletedTso);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                    }
                    if (coreDb.Delete_Exception_Repeat_Mapping(request_obj.coreProj, request_obj, request_obj) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                }
                else
                {
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDCR_Delete Delete_Org_Exception_Resource_Mapping(IDcResourceException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Delete resp = new DCR_Delete();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcResourceException)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_deleteOrgExceptionResourceMapping))
                {
                    //TODO: this function can be optimized
                    IDcExceptionID exceptionId = coreFactory.DcExceptionID(request_obj.coreProj);
                    exceptionId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    exceptionId.exceptionId = request_obj.exceptionId;
                    exceptionId.orgId = request_obj.orgId;

                    #region read all the exception tso mappings

                    IDcrTsoList exceptionTimePeriods = coreSc.Read_All_TimePeriods_For_Exception(exceptionId, validation, utils, coreSc, coreDb, coreFactory);
                    if (exceptionTimePeriods.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionTimePeriods);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    #endregion
                    #region read all the resource tso maps
                    IDcOrgResourceId resourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);;
                    resourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    resourceId.orgId = request_obj.orgId;
                    resourceId.resourceId = request_obj.resourceId;

                    IDcrTsoList resourceTsoList = coreSc.Read_All_TimePeriods_For_Resource(resourceId, validation, utils, coreSc, coreDb, coreFactory);
                    if (resourceTsoList.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourceTsoList);
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    #endregion
                    #region loop both of mappings and look for matches
                    var tsoFilteredByExId = exceptionTimePeriods.timeScaleList.Select(emp => emp.exceptionId).ToHashSet();
                    //List<BaseTSo> matchedTsos = resourceTsoList.timeScaleList.Where(product => tsoFilteredByExId.Contains(product.exceptionId)).ToList();
                    List<ITSO> matchedTsos = resourceTsoList.timeScaleList.Where(product => tsoFilteredByExId.Contains(product.exceptionId)).ToList();
                    #endregion
                    #region delete all of the matches
                    foreach (ITSO tsoToDelete in matchedTsos)
                    {
                        IDcTSOResourceId tsoId = coreFactory.DcTSOResourceId(request_obj.coreProj);
                        tsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        tsoId.orgId = tsoToDelete.orgId;
                        tsoId.tsoId = tsoToDelete.tsoId;
                        tsoId.resourceId = request_obj.resourceId;

                        IDCR_Delete deleteMap = coreSc.Delete_TimePeriod_Resource_Map(tsoId, coreDb);
                        if (deleteMap.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(deleteMap);
                            resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                            return resp;
                        }
                    }
                    #endregion
                    #region delete the final map of the resource and exception
                    //IDcResourceException resourceExId = coreFactory.DcResourceException(request_obj.coreProj);
                    //resourceExId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    //resourceExId.exceptionId = request_obj.exceptionId;
                    //resourceExId.orgId = request_obj.orgId;
                    //resourceExId.resourceId = request_obj.resourceId;
                    if (coreDb.Delete_Exception_Resource_Mapping(request_obj.coreProj, request_obj, request_obj) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.Result = ENUM_Cmd_Delete_State.Deleted;
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                        return resp;
                    }
                    #endregion
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                resp.Result = ENUM_Cmd_Delete_State.NotDeleted;
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_IdList resp = new DCR_IdList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {

                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readAllExceptionCalendarMappingsByExceptionID))
                {
                    IList<int> listOfCalendarIds = coreFactory.ListInt();
                    if (coreDb.Read_Exception_Calendar_Mappings(request_obj.coreProj, request_obj, listOfCalendarIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = listOfCalendarIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_All_Org_Exception_Repeat_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        { 
            DCR_IdList resp = new DCR_IdList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readAllExceptionRepeatMappings))
                {
                    IList<int> repeatIds = coreFactory.ListInt();

                    if (coreDb.Read_Exception_Repeat_Mappings(request_obj.coreProj, request_obj, repeatIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = repeatIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_IdList resp = new DCR_IdList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readAllExceptionResourceMappingsByExceptionID))
                {
                    IList<int> listOfResourceIds = coreFactory.ListInt();
                    if (coreDb.Read_Exception_Resource_Mappings(request_obj.coreProj, request_obj, request_obj, listOfResourceIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = listOfResourceIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_All_Org_Exception_TSoIds_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
        
            DCR_IdList resp = new DCR_IdList();
            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readAllOrgExceptionTSOsByExceptionID))
                {
                    IList<int> tsoIdList = coreFactory.ListInt();
                    if (coreDb.Read_All_Exception_TSos(request_obj.coreProj, request_obj.exceptionId,  tsoIdList) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = tsoIdList.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_All_Org_Exception_TSoIds_Filter_By_Repeat_ID(IDcExceptionRepeatId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
          
            DCR_IdList resp = new DCR_IdList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcExceptionRepeatId)))
            {
                if (request_obj.cmd_user_id == GeneralConfig.SYSTEM_WILDCARD_INT)
                {
                    IList<int> tsoIds = coreFactory.ListInt();
                    if (coreDb.Read_All_TimePeriod_Exception_Repeat_Maps(request_obj.coreProj, request_obj.exceptionId, request_obj.repeatId,  tsoIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = tsoIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
            }
            return resp;

        }

        public IDcrCalendarExceptionList Read_Org_Calendar_Exceptions_Between_TimeRange(IDcCalendarTimeRange request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
        
            IDcrCalendarExceptionList resp = new DCR_OrgCalendarExceptionList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcCalendarTimeRange)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readOrgCalendarExceptionsBetweenDateTime))
                {
                    //TODO: check the range isnt huge
                    //TODO: check that the date is not = to min or too far in the future
                    List<BaseExceptionComplete> exceptions;
                    //get the time periods between the range
                    //loop the time periods and identify the unique exception ids
                    //read the exception details

                    //DC_Org_Calendar_Time_Range modifiedRequest = new DC_Org_Calendar_Time_Range(request_obj.coreProj);
                    //modifiedRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;

                    IDcrTsoList calendarTimePeriods = coreSc.Read_TimePeriods_For_Calendar_Between_DateTime(request_obj, validation, utils, coreSc, coreDb, coreFactory);

                    if (calendarTimePeriods.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(calendarTimePeriods);
                        return resp;
                    }
                    Dictionary<int, List<ITSO>> unfiltered_timescaleObjs = new Dictionary<int, List<ITSO>>();
                    foreach (ITSO tso in calendarTimePeriods.timeScaleList)
                    {
                        if (tso.exceptionId != 0)
                        {
                            if (unfiltered_timescaleObjs.ContainsKey(tso.exceptionId))
                            {
                                unfiltered_timescaleObjs[tso.exceptionId].Add(tso);
                            }
                            else
                            {
                                List<ITSO> tmpList = new List<ITSO>();
                                tmpList.Add(tso);
                                unfiltered_timescaleObjs.Add(tso.exceptionId, tmpList);
                            }
                        }
                    }
                    foreach (int exId in unfiltered_timescaleObjs.Keys)
                    {
                        //this is wrong 
                        IDcExceptionID exRead = coreFactory.DcExceptionID(request_obj.coreProj);
                        exRead.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        exRead.exceptionId = exId;
                        exRead.orgId = request_obj.orgId;

                        IDcrException readException = coreSc.Read_Org_Exception_By_Exception_ID(exRead, validation, utils, coreSc, coreDb, coreFactory);
                        if (readException.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(readException);
                            return resp;
                        }

                        List<IExceptionComplete> resExs = new List<IExceptionComplete>();
                        IExceptionComplete baseExceptionData = new BaseExceptionComplete();
                        baseExceptionData.creatorId = readException.creatorId;
                        //baseExceptionData.creatorEmail = readException.creatorEmail;
                        baseExceptionData.durationMilliseconds = readException.durationMilliseconds;
                        baseExceptionData.exceptionId = readException.exceptionId;
                        baseExceptionData.exceptionTitle = readException.exceptionTitle;
                        baseExceptionData.orgId = readException.orgId;
                        baseExceptionData.start = readException.start;
                        baseExceptionData.end = readException.end;

                        IDcrIdList calendarMappedList = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(exRead, validation, utils, coreSc, coreDb, coreFactory);
                        if (calendarMappedList.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(calendarMappedList);
                            return resp;
                        }
                        baseExceptionData.calendarIdList = calendarMappedList.ListOfIDs;
                        foreach (ITSO tso in unfiltered_timescaleObjs[exId])
                        {
                            BaseTSo btso = new BaseTSo();
                            btso.exceptionId = tso.exceptionId;
                            btso.dateOfGeneration = tso.dateOfGeneration;
                            btso.durationMilliseconds = tso.durationMilliseconds;
                            btso.exceptionId = tso.exceptionId;
                            btso.orgId = tso.orgId;
                            btso.repeatId = tso.repeatId;
                            //btso.calendarIdList = tso.calendarIdList;
                            btso.start = tso.start;
                            btso.end = tso.end;
                            btso.tsoId = tso.tsoId;
                            btso.bookableSlot = false;
                            baseExceptionData.timeScaleList.Add(btso);

                        }

                        IDcrIdList repeatIds = coreSc.Read_All_Org_Exception_Repeat_Mappings_By_Exception_ID(exRead, validation, utils, coreSc, coreDb,coreFactory);
                        if (repeatIds.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(repeatIds);
                            return resp;
                        }
                        foreach (int repeatId in repeatIds.ListOfIDs)
                        {
                            IDcRepeatId repeatIdObj = coreFactory.DcRepeatId(request_obj.coreProj);
                            repeatIdObj.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            repeatIdObj.orgId = request_obj.orgId;
                            repeatIdObj.repeatId = repeatId;

                            IDcrRepeat repeatDetails = coreSc.Read_Repeat(repeatIdObj, validation, utils, coreSc, coreDb, coreFactory);
                            if (repeatDetails.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(repeatDetails);
                                return resp;
                            }
                            baseExceptionData.repeatRules.Add(new BaseRepeat(repeatDetails));
                        }
                        //resp.listOfExceptionCompletes.Add(baseExceptionData);
                        resExs.Add(baseExceptionData); 
                        resp.listOfExceptionCompletes = resExs;
                    }
                    resp.orgId = request_obj.orgId;
                    resp.calendarId = request_obj.calendarId;
                    resp.SetResponseOk();
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_Org_Calendar_Exception_Mappings_By_Calendar_ID(IDcCalendarId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_IdList resp = new DCR_IdList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcCalendarId)))
            {

                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readOrgCalendarExceptionMappingsByCalendarID))
                {
                    IList<int> listOfCalendarIds = coreFactory.ListInt();
                    if (coreDb.Read_Calendar_Exception_Mappings(request_obj.coreProj, request_obj, listOfCalendarIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = listOfCalendarIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
            }
            return resp;

        }

        public IDcrException Read_Org_Exception_By_Exception_ID(IDcExceptionID request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
          
            IDcrException resp = new DCR_OrgException();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcExceptionID)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readExceptionOptionsByExceptionID))
                {
                    IException exceptionData = coreFactory.Exception(request_obj.coreProj);
                    if (coreDb.Read_ExceptionOptions(request_obj.coreProj, request_obj, exceptionData) == ENUM_DB_Status.DB_SUCCESS)
                    {

                        IDcExceptionID exId = coreFactory.DcExceptionID(request_obj.coreProj);
                        exId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        exId.exceptionId = exceptionData.exceptionId;
                        exId.orgId = request_obj.orgId;
                        resp.exceptionId = exceptionData.exceptionId;
                        resp.creatorId = exceptionData.creatorId;
                        resp.exceptionTitle = exceptionData.exceptionTitle;
                        resp.timeZoneIANA = exceptionData.timeZoneIANA;
                        resp.orgId = request_obj.orgId;
                        resp.durationMilliseconds = exceptionData.durationMilliseconds;
                        resp.start = exceptionData.start;
                        resp.end = exceptionData.end;
                        //resp.creatorEmail = dbs.GetLoginNameFromUserID(request_obj.coreProj, resp.creatorId);
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;
        }

        public IDcrIdList Read_Org_Resource_Exception_Mappings_By_Resource_ID(IDcOrgResourceId request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
          
            DCR_IdList resp = new DCR_IdList();
            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcOrgResourceId)))
            {

                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readOrgResourceExceptionMappingsByResourceID))
                {
                    IList<int> listOfResourceIds = coreFactory.ListInt();
                    if (coreDb.Read_Resource_Exception_Mappings(request_obj.coreProj, request_obj, request_obj, listOfResourceIds) == ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.ListOfIDs = listOfResourceIds.ToList();
                        resp.SetResponseOk();
                    }
                    else
                    {
                        resp.SetResponseServerError();
                        return resp;
                    }
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;

        }

        public IDcrResourceExceptionCompleteList Read_Resources_Exceptions_Between_TimeRange(IDcOrgResourcesTimeRange request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
       
            IDcrResourceExceptionCompleteList resp = new DCR_OrgResourcesExceptionCompleteList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDcOrgResourcesTimeRange)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readResourceExceptionBetweenTimeRange))
                {
                    //TODO: check the range isnt huge
                    //TODO: check that the date is not = to min or too far in the future
                    List<BaseExceptionComplete> exceptions;
                    //get the time periods between the range
                    //loop the time periods and identify the unique exception ids
                    //read the exception details
                    //DC_Org_Resources_Time_Range modifiedRequest = new DC_Org_Resources_Time_Range(request_obj.coreProj, request_obj);
                    //DC_Org_Resources_Time_Range modifiedRequest = new DC_Org_Resources_Time_Range(request_obj.coreProj);
                    //modifiedRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;

                    IDcrResourcesTsoList resourceTimePeriods = coreSc.Read_TimePeriods_For_Resources_Between_DateTime(request_obj, validation, utils, coreSc, coreDb, coreFactory);
                    if (resourceTimePeriods.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourceTimePeriods);
                        return resp;
                    }
                    foreach (int resourceId in request_obj.resourceIdList)
                    {
                        Dictionary<int, IList<ITSO>> unfiltered_timescaleObjs = new Dictionary<int, IList<ITSO>>();
                        //foreach (BaseTSo tso in resourceTimePeriods.resourceTSOList[resourceId])
                        foreach (ITSO tso in resourceTimePeriods.resourceTSOList[resourceId])
                        {
                            if (tso.exceptionId != 0)
                            {
                                if (unfiltered_timescaleObjs.ContainsKey(tso.exceptionId))
                                {
                                    unfiltered_timescaleObjs[tso.exceptionId].Add(tso);
                                }
                                else
                                {
                                    IList<ITSO> tmpList = coreFactory.ListITSO();
                                    tmpList.Add(tso);
                                    unfiltered_timescaleObjs.Add(tso.exceptionId, tmpList);
                                }
                            }
                        }
                        //List<BaseExceptionComplete> resExs = new List<BaseExceptionComplete>();
                        IList<IExceptionComplete> resExs = coreFactory.ListExceptionComplete();
                        foreach (int exId in unfiltered_timescaleObjs.Keys)
                        {
                            //this is wrong 
                            IDcExceptionID dcExceptionID = coreFactory.DcExceptionID(request_obj.coreProj);
                            dcExceptionID.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcExceptionID.exceptionId = exId;
                            dcExceptionID.orgId = request_obj.orgId;

                            IDcrException readException = coreSc.Read_Org_Exception_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                            if (readException.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(readException);
                                return resp;
                            }
                            //BaseExceptionComplete baseExceptionData = new BaseExceptionComplete();
                            IExceptionComplete baseExceptionData = coreFactory.ExceptionComplete();
                            baseExceptionData.creatorId = readException.creatorId;
                            //baseExceptionData.creatorEmail = readException.creatorEmail;
                            baseExceptionData.durationMilliseconds = readException.durationMilliseconds;
                            baseExceptionData.exceptionId = readException.exceptionId;
                            baseExceptionData.exceptionTitle = readException.exceptionTitle;
                            baseExceptionData.orgId = readException.orgId;
                            baseExceptionData.start = readException.start;
                            baseExceptionData.end = readException.end;

                            IDcrIdList resourceMappedList = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                            if (resourceMappedList.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(resourceMappedList);
                                return resp;
                            }
                            baseExceptionData.resourceIdList = resourceMappedList.ListOfIDs;

                            IDcrIdList calendarMappedList = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                            if (calendarMappedList.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(calendarMappedList);
                                return resp;
                            }
                            baseExceptionData.calendarIdList = calendarMappedList.ListOfIDs;
                            foreach (ITSO tso in unfiltered_timescaleObjs[exId])
                            {
                                ITSO btso = coreFactory.ITSO();
                                btso.exceptionId = tso.exceptionId;
                                btso.dateOfGeneration = tso.dateOfGeneration;
                                btso.durationMilliseconds = tso.durationMilliseconds;
                                btso.exceptionId = tso.exceptionId;
                                btso.orgId = tso.orgId;
                                btso.repeatId = tso.repeatId;
                                //btso.resourceIdList = tso.resourceIdList;
                                btso.start = tso.start;
                                btso.end = tso.end;
                                btso.tsoId = tso.tsoId;
                                btso.bookableSlot = false;
                                baseExceptionData.timeScaleList.Add(btso);

                            }
                            resExs.Add(baseExceptionData);
                        }
                        IResourceExceptionComplete resExCompl = coreFactory.ResourceExceptionComplete();
                        resExCompl.resourceId = resourceId;
                        // Added by saddam
                        resExCompl.listOfExceptionCompletes = resExs.ToList();
                        //resExCompl.listOfExceptionCompletes = resExs;
                        resp.listOfExceptionCompletes.Add(resExCompl);
                    }

                    resp.orgId = request_obj.orgId;
                    resp.SetResponseOk();
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
            }
            return resp;
        }

        public IDcrResourceExceptionCompleteList Read_Resource_Exceptions_Between_TimeRange(IDCResourceTimeRange request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            IDcrResourceExceptionCompleteList resp = new DCR_OrgResourcesExceptionCompleteList();

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj, typeof(IDCResourceTimeRange)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_readResourceExceptionBetweenTimeRange))
                {
                    //TODO: check the range isnt huge
                    //TODO: check that the date is not = to min or too far in the future
                    List<BaseExceptionComplete> exceptions;
                    //get the time periods between the range
                    //loop the time periods and identify the unique exception ids
                    //read the exception details
                    //DC_Org_Resource_Time_Range modifiedRequest = new DC_Org_Resource_Time_Range(request_obj.coreProj, request_obj);
                    //DC_Org_Resource_Time_Range modifiedRequest = new DC_Org_Resource_Time_Range(request_obj.coreProj);
                    //modifiedRequest.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;

                    IDcrTsoList resourceTimePeriods = coreSc.Read_TimePeriods_For_Resource_Between_DateTime(request_obj, validation, utils,  coreSc, coreDb, coreFactory);
                    if (resourceTimePeriods.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(resourceTimePeriods);
                        return resp;
                    }
                    Dictionary<int, IList<ITSO>> unfiltered_timescaleObjs = new Dictionary<int, IList<ITSO>>();
                    //foreach (BaseTSo tso in resourceTimePeriods.timeScaleList)
                    foreach (ITSO tso in resourceTimePeriods.timeScaleList)
                    {
                        if (tso.exceptionId != 0)
                        {
                            if (unfiltered_timescaleObjs.ContainsKey(tso.exceptionId))
                            {
                                unfiltered_timescaleObjs[tso.exceptionId].Add(tso);
                            }
                            else
                            {
                                IList<ITSO> tmpList = coreFactory.ListITSO();
                                tmpList.Add(tso);
                                unfiltered_timescaleObjs.Add(tso.exceptionId, tmpList);
                            }
                        }
                    }
                    foreach (int exId in unfiltered_timescaleObjs.Keys)
                    {
                        //this is wrong 
                        IDcExceptionID exRead = coreFactory.DcExceptionID(request_obj.coreProj);
                        exRead.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        exRead.exceptionId = exId;
                        exRead.orgId = request_obj.orgId;

                        IDcrException readException = coreSc.Read_Org_Exception_By_Exception_ID(exRead, validation, utils, coreSc, coreDb, coreFactory);

                        if (readException.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(readException);
                            return resp;
                        }
                        IList<IExceptionComplete> resExs = coreFactory.ListExceptionComplete();
                        IExceptionComplete baseExceptionData = coreFactory.ExceptionComplete();
                        baseExceptionData.creatorId = readException.creatorId;
                        //baseExceptionData.creatorEmail = readException.creatorEmail;
                        baseExceptionData.durationMilliseconds = readException.durationMilliseconds;
                        baseExceptionData.exceptionId = readException.exceptionId;
                        baseExceptionData.exceptionTitle = readException.exceptionTitle;
                        baseExceptionData.orgId = readException.orgId;
                        baseExceptionData.start = readException.start;
                        baseExceptionData.end = readException.end;
                        //DCR_Id_List resourceMappedList = SC_Org_Exceptions.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(exRead);
                        IDcrIdList resourceMappedList = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(exRead, validation, utils, coreSc, coreDb, coreFactory);
                        if (resourceMappedList.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(resourceMappedList);
                            return resp;
                        }
                        baseExceptionData.resourceIdList = resourceMappedList.ListOfIDs;
                        //DCR_Id_List calendarMappedList = SC_Org_Exceptions.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(exRead);
                        IDcrIdList calendarMappedList = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(exRead, validation, utils, coreSc, coreDb, coreFactory);
                        if (calendarMappedList.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(calendarMappedList);
                            return resp;
                        }
                        baseExceptionData.calendarIdList = calendarMappedList.ListOfIDs;
                        foreach (ITSO tso in unfiltered_timescaleObjs[exId])
                        {
                            ITSO btso = coreFactory.ITSO();
                            btso.exceptionId = tso.exceptionId;
                            btso.dateOfGeneration = tso.dateOfGeneration;
                            btso.durationMilliseconds = tso.durationMilliseconds;
                            btso.exceptionId = tso.exceptionId;
                            btso.orgId = tso.orgId;
                            btso.repeatId = tso.repeatId;
                            //btso.resourceIdList = tso.resourceIdList;
                            btso.start = tso.start;
                            btso.end = tso.end;
                            btso.tsoId = tso.tsoId;
                            btso.bookableSlot = false;
                            baseExceptionData.timeScaleList.Add(btso);

                        }

                        IResourceExceptionComplete resExCompl = coreFactory.ResourceExceptionComplete();
                        
                        //resp.listOfExceptionCompletes.Add(baseExceptionData);
                        //resp.listOfExceptionCompletes.Add(baseExceptionData);
                        resExs.Add(baseExceptionData);

                        resExCompl.listOfExceptionCompletes = resExs.ToList();
                        //resp.listOfExceptionCompletes[0].listOfExceptionCompletes = resExs;
                        resp.listOfExceptionCompletes.Add(resExCompl);
                    }

                    resp.orgId = request_obj.orgId;
                    //resp.resourceId = request_obj.resourceId;
                    resp.SetResponseOk();
                }
                else
                {
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.SetResponseInvalidParameter();
            }
            return resp;

        }

        public IDCR_Update Update_Org_Exception(IDcUpdateOrgException request_obj, IValidation validation, IUtils utils, ICoreSc coreSc, ICoreDatabase coreDb, IFactoryCore coreFactory)
        {
            DCR_Update resp = new DCR_Update();

            //TODO: check that exceptionId adjustments are allowed if the exceptionId being modified is ok
            //TODO: infact i think you have alot more validation to do when updating the exceptionId times
            //TODO: make it more generic so the same functionality can be used for exceptions
            //TODO: check that the exceptionId is owned by the command issuer

            if (validation.Is_Valid(request_obj.coreProj, coreSc, coreDb, utils,coreFactory, request_obj, typeof(IDcUpdateOrgException)))
            {
                if (validation.Permissions_User_Can_Do_Core_Action(request_obj.coreProj, coreSc, coreDb, utils, coreFactory, request_obj.cmd_user_id, request_obj.orgId, ENUM_Core_Function.CF_updateOrgException))
                {
                    if (request_obj.calendarIdList.Count == 0 && request_obj.resourceIdList.Count == 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #region read the previous exception options
                    IDcExceptionID dcExceptionID = coreFactory.DcExceptionID(request_obj.coreProj);
                    dcExceptionID.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    dcExceptionID.exceptionId = request_obj.exceptionId;
                    dcExceptionID.orgId = request_obj.orgId;

                    IDcrException exceptionOptions = coreSc.Read_Org_Exception_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                    if (exceptionOptions.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(exceptionOptions);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion
                    #region compare the exception options for differences and make sure RO are the same
                    //check that the read only stuff hasnt changed
                    if (request_obj.creatorId != exceptionOptions.creatorId ||
                        request_obj.orgId != exceptionOptions.orgId
                      )
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    //the rest is either checked later or irrelevant and can be just written to the database
                    #endregion

                    #region read the previous exception resource mappings

                    IDcrIdList currentExceptionResourceMappings = coreSc.Read_All_Org_Exception_Resource_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                    if (currentExceptionResourceMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(currentExceptionResourceMappings);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion
                    #region read the previous exception calendar mappings

                    IDcrIdList currentExceptionCalendarMappings = coreSc.Read_All_Org_Exception_Calendar_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb,coreFactory);
                    //if (currentExceptionResourceMappings.func_status != ENUM_Cmd_Status.ok)
                    if (currentExceptionCalendarMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(currentExceptionCalendarMappings);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion

                    #region read the current repeat instances

                    IDcrTsoList originalExceptionTSOList = coreSc.Read_All_TimePeriods_For_Exception(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                    if (originalExceptionTSOList.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(originalExceptionTSOList);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    if (originalExceptionTSOList.timeScaleList.Count != 1)
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion

                    #region make sure calendars and resources arent both mapped
                    if (request_obj.calendarIdList.Count > 0 && request_obj.resourceIdList.Count > 0)
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion


                    #region we are definitely making an update so lets generate the new tso objects
                    IInstantStartStop appointmentTR = coreFactory.InstantStartStop();

                    appointmentTR.start = InstantPattern.ExtendedIsoPattern.Parse(request_obj.start).Value;
                    appointmentTR.stop = InstantPattern.ExtendedIsoPattern.Parse(request_obj.end).Value;

                    IList<IInstantStartStop> allTSOs = coreFactory.ListInstantStartStop();

                    allTSOs.Add(appointmentTR);

                    IList<IInstantStartStop> repeatTSOs = coreFactory.ListInstantStartStop();
                    for (int i = 0; i < request_obj.repeatRuleOptions.Count; i++)
                    {
                        //if there are repeat rules then generate the repeated time periods
                        ITimeStartEnd trange = coreFactory.TimeStartEnd();
                        trange.start = request_obj.start;
                        trange.end = request_obj.end;

                        List<IInstantStartStop> fullTsoList = utils.GenerateRepeatTimePeriods(request_obj.coreProj, coreSc, trange, request_obj.repeatRuleOptions[i], request_obj.timeZoneIANA, true, coreDb, coreFactory);
                        //if (fullTsoList.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(fullTsoList);
                        //    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        //    return resp;
                        //}
                        //foreach (BaseInstantStartStop tr in fullTsoList.TimePeriods)



                        foreach (IInstantStartStop tr in fullTsoList)
                        {
                            //i think this will need to be removed as it will occur below
                            //allTSOs.Add(tr);
                            repeatTSOs.Add(tr);
                        }
                    }
                    #endregion

                    //depending on the update type we need to generate a different set of events

                    #region Update according to ENUM_Repeat_UpdateType

                    IDcrTSO tsoDetail = null;

                    if (request_obj.tsoId > 0)
                    {
                        IDcTsoId dcTsoId = coreFactory.DcTsoId(request_obj.coreProj);
                        //call this witht he request_obj.tsoId
                        dcTsoId.tsoId = request_obj.tsoId;
                        dcTsoId.orgId = request_obj.orgId;
                        dcTsoId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        tsoDetail = coreSc.Read_TSo(dcTsoId, validation, utils,  coreSc, coreDb, coreFactory);
                        if (tsoDetail.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(tsoDetail);
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                    }
                    if (request_obj.updateExceptionType == ENUM_Repeat_UpdateType.Update_All)
                    {
                        //no special logic we just generate a new set with the new data
                        foreach (IInstantStartStop tr in repeatTSOs)
                        {
                            allTSOs.Add(tr);
                        }
                    }
                    else if (request_obj.updateExceptionType == ENUM_Repeat_UpdateType.Update_All_After_Tsoid)
                    {
                        //if this is set we need to only update the tso's which appear after the tsoId specified in the request object which should be found in originalAppointmentTSOList
                        //loop the originalAppointmentTSOList
                        //add all of the entries into allTSOs until reaching the tsoId specified
                        //then add all of the entries in repeatTSOs which occur after the end date of the tsoid specified into allTSOs
                        if (tsoDetail == null)
                        {
                            resp.SetResponseServerError();
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                        List<ITSO> listOfITSO = originalExceptionTSOList.timeScaleList.Where(x => InstantPattern.ExtendedIsoPattern.Parse(x.end).Value < InstantPattern.ExtendedIsoPattern.Parse(tsoDetail.start).Value).ToList();

                        allTSOs.AddRange(utils.CONVERT_ITSOListToInstantList(listOfITSO));
                        allTSOs.AddRange(repeatTSOs.Where(x => x.start > InstantPattern.ExtendedIsoPattern.Parse(tsoDetail.end).Value));

                    }
                    else if (request_obj.updateExceptionType == ENUM_Repeat_UpdateType.Update_Single_Tsoid)
                    {
                        //if this is set we need to only update the single tso's which match the tsoId specified in originalAppointmentTSOList
                        //loop the originalAppointmentTSOList
                        //add all of the entries into allTSOs until reaching the tsoId specified 
                        //modify the tsoid event
                        //continue looping and add into allTSOs
                        if (tsoDetail == null)
                        {
                            resp.SetResponseServerError();
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                        List<ITSO> listOfITSO = originalExceptionTSOList.timeScaleList.Where(x => x.tsoId == tsoDetail.tsoId).ToList();
                        allTSOs.AddRange(utils.CONVERT_ITSOListToInstantList(listOfITSO));
                        ITSO tsoNew = new BaseTSo((ITSO)tsoDetail);
                        tsoNew.start = request_obj.start;
                        tsoNew.end = request_obj.end;
                        allTSOs.AddRange(utils.CONVERT_ITSOListToInstantList(new List<ITSO>() { tsoNew }));

                    }
                    else
                    {
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }

                    #endregion
                    //Below here no need to change, it just creates the new objects from allTSOs

                    #region check that the repeat config doesnt cause an overlap
                    //foreach (BaseInstantStartStop trToCheck in allTSOs)
                    int index = 0;
                    foreach (IInstantStartStop trToCheck in allTSOs)
                    {
                        //List<BaseInstantStartStop> alreadyAllocatedTSOList = new List<BaseInstantStartStop>();
                        IList<IInstantStartStop> alreadyAllocatedTSOList = coreFactory.ListInstantStartStop();

                        alreadyAllocatedTSOList.Add(trToCheck);
                        //List<BaseInstantStartStop> filterdTSOs = Utils.COPY_TimePeriodCollection(allTSOs);
                        IList<IInstantStartStop> filterdTSOs = utils.COPY_TimePeriodCollection(allTSOs);
                        filterdTSOs.RemoveAt(index);
                        //DCR_TimePeriod_List selfConflictList = Utils.GetConflictingTimePeriods(alreadyAllocatedTSOList, filterdTSOs);
                        List<IInstantStartStop> selfConflictList = utils.GetConflictingTimePeriods(alreadyAllocatedTSOList, filterdTSOs);


                        //if (selfConflictList.func_status != ENUM_Cmd_Status.ok)
                        //{
                        //    resp.StatusAndMessage_CopyFrom(selfConflictList);
                        //    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        //    return resp;
                        //}
                        if (selfConflictList.Count > 0)
                        {
                            resp.SetResponseInvalidParameter();
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                        index++;
                    }
                    #endregion

                    #region we are linking calendars
                    if (request_obj.calendarIdList.Count > 0)
                    {
                        IDcCalendarId calendarTimeRange = coreFactory.DcCalendarId(request_obj.coreProj);
                        calendarTimeRange.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        //calendarTimeRange.start = GeneralConfig.DEFAULT_SYSTEM_MIN_DATE;
                        //calendarTimeRange.end = GeneralConfig.DEFAULT_SYSTEM_MAX_DATE;
                        calendarTimeRange.orgId = request_obj.orgId;
                        foreach (int calendarId in request_obj.calendarIdList)
                        {
                            calendarTimeRange.calendarId = calendarId;

                            IDcrTsoList calendarTSOs = coreSc.Read_All_TimePeriods_For_Calendar(calendarTimeRange, validation, utils, coreSc, coreDb, coreFactory);
                            if (calendarTSOs.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(calendarTSOs);
                                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                return resp;
                            }
                            #region filter out the tso's belonging to the exception we are updating as they will be changed or are not valid conflicts
                            //List<BaseTSo> listOfTimePeriodsBelongingToOtherExceptions = new List<BaseTSo>();
                            IList<ITSO> listOfTimePeriodsBelongingToOtherExceptions = coreFactory.ListITSO();
                            //foreach (BaseTSo tsoData in calendarTSOs.timeScaleList)
                            foreach (ITSO tsoData in calendarTSOs.timeScaleList)
                            {
                                if (tsoData.exceptionId != request_obj.exceptionId)
                                {
                                    listOfTimePeriodsBelongingToOtherExceptions.Add(tsoData);
                                }
                            }
                            #endregion
                            //DCR_TimePeriod_List conflictList = Utils.GetConflictingTimePeriods(Utils.CONVERT_BaseTSOToInstantList(listOfTimePeriodsBelongingToOtherExceptions), allTSOs);
                            List<IInstantStartStop> conflictList = utils.GetConflictingTimePeriods(utils.CONVERT_ITSOListToInstantList(listOfTimePeriodsBelongingToOtherExceptions), allTSOs);
                            //if (conflictList.func_status != ENUM_Cmd_Status.ok)
                            //{
                            //    resp.StatusAndMessage_CopyFrom(conflictList);
                            //    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            //    return resp;
                            //}
                            if (conflictList.Count > 0)
                            {
                                resp.SetResponsePermissionDenied();
                                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                return resp;
                            }
                            #region we need to check all the objects the calendar is also linked to can accept the new modifications
                            #region check the resources can take the new calendar exceptions
                            IDcCalendarId dcCalendarId = coreFactory.DcCalendarId(request_obj.coreProj);
                            dcCalendarId.calendarId = calendarId;
                            dcCalendarId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcCalendarId.orgId = request_obj.orgId;

                            IDcrIdList calendarResourceIdList = coreSc.Read_All_Org_Calendar_Resource_Mappings_By_Calendar_ID(dcCalendarId, validation, utils, coreSc, coreDb, coreFactory);
                            if (calendarResourceIdList.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(calendarResourceIdList);
                                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                return resp;
                            }
                            IDcOrgResourceId dcResourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);
                            dcResourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                            dcResourceId.orgId = request_obj.orgId;
                            foreach (int resId in calendarResourceIdList.ListOfIDs)
                            {
                                dcResourceId.resourceId = resId;

                                IDcrTsoList resTsoList = coreSc.Read_All_TimePeriods_For_Resource(dcResourceId, validation, utils, coreSc, coreDb, coreFactory);
                                if (resTsoList.func_status != ENUM_Cmd_Status.ok)
                                {
                                    resp.StatusAndMessage_CopyFrom(resTsoList);
                                    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                    return resp;
                                }
                                //List<BaseInstantStartStop> tpc = new List<BaseInstantStartStop>();
                                //foreach (BaseTSo tsoDetails in resTsoList.timeScaleList)
                                IList<IInstantStartStop> tpc = coreFactory.ListInstantStartStop();
                                foreach (ITSO tsoDetails in resTsoList.timeScaleList)
                                {
                                    if (tsoDetails.exceptionId != request_obj.exceptionId)
                                    {
                                        tpc.Add(utils.CONVERT_ITSoToTimeRange(tsoDetails));
                                    }
                                }
                                //DCR_TimePeriod_List conflictedTimePeriods = Utils.GetConflictingTimePeriods(tpc, allTSOs);
                                List<IInstantStartStop> conflictedTimePeriods = utils.GetConflictingTimePeriods(tpc, allTSOs);
                                //if (conflictedTimePeriods.func_status != ENUM_Cmd_Status.ok)
                                //{
                                //    resp.StatusAndMessage_CopyFrom(conflictedTimePeriods);
                                //    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                //    return resp;
                                //}
                                if (conflictedTimePeriods.Count > 0)
                                {
                                    resp.SetResponseInvalidParameter();
                                    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                    return resp;
                                }
                            }
                            #endregion
                            #endregion
                        }
                    }
                    #endregion
                    #region we are linking resources
                    else if (request_obj.resourceIdList.Count > 0)
                    {
                        IDcOrgResourceId dcOrgResourceId = coreFactory.DcOrgResourceId(request_obj.coreProj);;
                        dcOrgResourceId.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                        dcOrgResourceId.orgId = request_obj.orgId;
                        foreach (int resId in request_obj.resourceIdList)
                        {
                            dcOrgResourceId.resourceId = resId;

                            IDcrTsoList resTsoList = coreSc.Read_All_TimePeriods_For_Resource(dcOrgResourceId, validation, utils, coreSc, coreDb, coreFactory);
                            if (resTsoList.func_status != ENUM_Cmd_Status.ok)
                            {
                                resp.StatusAndMessage_CopyFrom(resTsoList);
                                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                return resp;
                            }
                            //List<BaseInstantStartStop> tpc = new List<BaseInstantStartStop>();
                            //foreach (BaseTSo tsoDetails in resTsoList.timeScaleList)
                            IList<IInstantStartStop> tpc = coreFactory.ListInstantStartStop();
                            foreach (ITSO tsoDetails in resTsoList.timeScaleList)
                            {
                                if (tsoDetails.exceptionId != request_obj.exceptionId)
                                {
                                    tpc.Add(utils.CONVERT_ITSoToTimeRange(tsoDetails));
                                }
                            }
                            //DCR_TimePeriod_List conflictedTimePeriods = Utils.GetConflictingTimePeriods(tpc, allTSOs);
                            List<IInstantStartStop> conflictedTimePeriods = utils.GetConflictingTimePeriods(tpc, allTSOs);
                            //if (conflictedTimePeriods.func_status != ENUM_Cmd_Status.ok)
                            //{
                            //    resp.StatusAndMessage_CopyFrom(conflictedTimePeriods);
                            //    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            //    return resp;
                            //}
                            if (conflictedTimePeriods.Count > 0)
                            {
                                resp.SetResponseInvalidParameter();
                                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                                return resp;
                            }
                        }
                    }
                    #endregion
                    #region there are no resources or calendars to be linked
                    else
                    {
                        //no need to verify or check anything
                    }
                    #endregion

                    #region delete all the resource mappings to the exception

                    IDCR_Delete deleteExceptionResourceMappings = coreSc.Delete_All_Org_Exception_Resource_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                    if (deleteExceptionResourceMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deleteExceptionResourceMappings);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion
                    #region delete all the calendar mappings to the exception

                    IDCR_Delete deleteExceptionCalendarMappings = coreSc.Delete_All_Org_Exception_Calendar_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb,coreFactory);
                    if (deleteExceptionCalendarMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deleteExceptionCalendarMappings);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion
                    #region delete all the repeat rules mapped to the exception

                    IDCR_Delete deleteExceptionRepMappings = coreSc.Delete_All_Org_Exception_Repeat_Mappings_By_Exception_ID(dcExceptionID, validation, utils, coreSc, coreDb, coreFactory);
                    if (deleteExceptionRepMappings.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(deleteExceptionRepMappings);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion

                    #region if the exception options have changed update them
                    if (coreDb.Update_ExceptionOptions(request_obj.coreProj, request_obj) != ENUM_DB_Status.DB_SUCCESS)
                    {
                        resp.SetResponseServerError();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion

                    #region update the only remaining TSO and original exception tso to the details updated above
                    //DCR_Org_TSO_List originalExceptionTSOList = SC_TSO.Read_All_TimePeriods_For_Exception(exceptionId);
                    if (originalExceptionTSOList.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(originalExceptionTSOList);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    //if (originalExceptionTSOList.timeScaleList.Count != 1)
                    if (originalExceptionTSOList.timeScaleList == null || originalExceptionTSOList.timeScaleList.Count == 0)
                    {
                        //resp.SetResponseServerError();
                        resp.SetResponseInvalidParameter();
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    IDcUpdateTSO updateTSO = coreFactory.DcUpdateTSO(request_obj.coreProj);
                    updateTSO.exceptionId = request_obj.exceptionId;
                    updateTSO.tsoId = originalExceptionTSOList.timeScaleList[0].tsoId;
                    updateTSO.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    updateTSO.dateOfGeneration = DateTime.Now.OrijDTStr();
                    updateTSO.durationMilliseconds = request_obj.durationMilliseconds;
                    updateTSO.end = request_obj.end;
                    updateTSO.appointmentId = 0;
                    updateTSO.orgId = request_obj.orgId;
                    updateTSO.repeatId = 0;
                    updateTSO.start = request_obj.start;

                    IDCR_Update updatedTSO = coreSc.Update_TimePeriod(updateTSO, validation, utils,  coreSc, coreDb, coreFactory);
                    if (updatedTSO.func_status != ENUM_Cmd_Status.ok)
                    {
                        resp.StatusAndMessage_CopyFrom(updatedTSO);
                        resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                        return resp;
                    }
                    #endregion

                    #region update the repeat rules
                    //create repeat then map it
                    IDcMapRepeatException createExceptionRepMapping = coreFactory.DcMapRepeatException(request_obj.coreProj);
                    createExceptionRepMapping.exceptionId = request_obj.exceptionId;
                    createExceptionRepMapping.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createExceptionRepMapping.orgId = request_obj.orgId;
                    //foreach (BaseRepeatOptions repeatRule in request_obj.repeatRuleOptions)
                    foreach (IRepeatOptions repeatRule in request_obj.repeatRuleOptions)
                    {
                        //DC_Create_Org_RepeatRule createRepeatRule = new DC_Create_Org_RepeatRule(request_obj.coreProj, repeatRule);
                        IDcCreateRepeat createRepeatRule = coreFactory.DcCreateRepeat(request_obj.coreProj);
                        createRepeatRule.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;

                        IDCR_Added createdRepeatRule = coreSc.Create_Repeat(createRepeatRule, validation, utils, coreSc, coreDb, coreFactory);
                        if (createdRepeatRule.func_status != ENUM_Cmd_Status.ok)
                        {
                            //resp.SetResponseServerError();
                            resp.StatusAndMessage_CopyFrom(createdRepeatRule);
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                        createExceptionRepMapping.repeatId = createdRepeatRule.NewRecordID;
                        createExceptionRepMapping.creatorId = request_obj.creatorId;

                        IDCR_Added createdAppResMap = coreSc.Create_Org_Exception_Repeat_Map(createExceptionRepMapping, validation, utils, coreSc, coreDb, coreFactory);
                        if (createdAppResMap.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(createdAppResMap);
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                    }
                    #endregion
                    #region update the resource maps

                    IDcResourceException createExceptionMapping = coreFactory.DcResourceException(request_obj.coreProj);
                    createExceptionMapping.exceptionId = request_obj.exceptionId;
                    createExceptionMapping.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createExceptionMapping.orgId = request_obj.orgId;
                    foreach (int resourceId in request_obj.resourceIdList)
                    {
                        createExceptionMapping.resourceId = resourceId;

                        IDCR_Added createdAppResMap = coreSc.Create_Org_Exception_Resource_Mapping(createExceptionMapping, validation, utils, coreSc, coreDb, coreFactory);
                        if (createdAppResMap.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(createdAppResMap);
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                    }
                    #endregion

                    #region update the calendar mappings

                    IDcCalendarExceptionId createCalendarExceptionMapping = coreFactory.DcCalendarExceptionId(request_obj.coreProj);
                    createCalendarExceptionMapping.exceptionId = request_obj.exceptionId;
                    createCalendarExceptionMapping.cmd_user_id = GeneralConfig.SYSTEM_WILDCARD_INT;
                    createCalendarExceptionMapping.orgId = request_obj.orgId;
                    foreach (int calendarId in request_obj.calendarIdList)
                    {
                        createCalendarExceptionMapping.calendarId = calendarId;

                        IDCR_Added createdAppResMap = coreSc.Create_Org_Exception_Calendar_Mapping(createCalendarExceptionMapping, validation, utils, coreSc, coreDb, coreFactory);
                        if (createdAppResMap.func_status != ENUM_Cmd_Status.ok)
                        {
                            resp.StatusAndMessage_CopyFrom(createdAppResMap);
                            resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                            return resp;
                        }
                    }
                    #endregion
                    resp.SetResponseOk();
                    resp.Result = ENUM_Cmd_Update_Result.Updated;
                }
                else
                {
                    resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                    resp.SetResponsePermissionDenied();
                    return resp;
                }
            }
            else
            {
                resp.Result = ENUM_Cmd_Update_Result.Not_Updated;
                resp.SetResponseInvalidParameter();
                return resp;
            }
            return resp;

        }
    }
}
