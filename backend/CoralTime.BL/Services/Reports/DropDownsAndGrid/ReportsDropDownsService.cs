﻿using CoralTime.Common.Constants;
using CoralTime.DAL.ConvertModelToView;
using CoralTime.DAL.Models;
using CoralTime.ViewModels.Reports.Request.Grid;
using CoralTime.ViewModels.Reports.Responce.DropDowns.Filters;
using CoralTime.ViewModels.Reports.Responce.DropDowns.GroupBy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoralTime.BL.Services.Reports.DropDownsAndGrid
{
    public partial class ReportsService
    {
        private readonly List<ReportDropDownGroupBy> _dropDownGroupBy = new List<ReportDropDownGroupBy>
        {
            new ReportDropDownGroupBy
            {
                Id = (int) Constants.ReportsGroupBy.Project,
                Description = Constants.ReportsGroupBy.Project.ToString()
            },

            new ReportDropDownGroupBy
            {
                Id = (int) Constants.ReportsGroupBy.Member,
                Description = Constants.ReportsGroupBy.Member.ToString()
            },

            new ReportDropDownGroupBy
            {
                Id = (int) Constants.ReportsGroupBy.Date,
                Description = Constants.ReportsGroupBy.Date.ToString()
            },

            new ReportDropDownGroupBy
            {
                Id = (int) Constants.ReportsGroupBy.Client,
                Description = Constants.ReportsGroupBy.Client.ToString()
            }
        };

        public ReportDropDownsView ReportsDropDowns()
        {
            var user = Uow.UserRepository.GetRelatedUserByName(InpersonatedUserName);
            var memberByUserName = Uow.MemberRepository.LinkedCacheGetByName(InpersonatedUserName);

            var reportDropDowns = new ReportDropDownsView
            {
                Values = CreateDropDownValues(memberByUserName),
                CurrentQuery = CreateDropDownCurrentQuery(memberByUserName.Id)
            };

            return reportDropDowns;
        }

        private ReportDropDownValues CreateDropDownValues(Member member)
        {
            var managerRoleId = Uow.ProjectRoleRepository.GetManagerRoleId();
            var memberRoleId = Uow.ProjectRoleRepository.GetMemberRoleId();

            var projectsOfClients = new List<Project>();
            var clientsFromProjectOfClients = new List<Client>();
            var members = Uow.MemberRepository.LinkedCacheGetList();

            #region GetProjects allProjectsForAdmin or projectsWithAssignUsersAndPublicProjects.

            if (member.User.IsAdmin)
            {
                var allProjectsForAdmin = Uow.ProjectRepository.LinkedCacheGetList().ToList();
                projectsOfClients = allProjectsForAdmin;
            }
            else
            {
                var projectsWithAssignUsersAndPublicProjects = Uow.ProjectRepository.LinkedCacheGetList()
                    .Where(x => x.MemberProjectRoles.Select(z => z.MemberId).Contains(member.Id) || !x.IsPrivate).ToList();

                projectsOfClients.AddRange(projectsWithAssignUsersAndPublicProjects);
            }

            #endregion

            #region Get Clients from Projects of clients.

            // 1. Get all clients from targeted projects where project is assign to client.
            var clientsWithProjects = projectsOfClients.Where(x => x.Client != null).Select(x => x.Client).Distinct().ToList();

            foreach (var client in clientsWithProjects)
            {
                client.Projects = client.Projects.Where(proj => projectsOfClients.Select(pc => pc.Id).Contains(proj.Id)).ToList();
            }

            clientsFromProjectOfClients.AddRange(clientsWithProjects);

            // 2. Get all projects where project is not assign to client and create client "WithoutClients" that we add projects to it.
            var hasClientsWithoutProjects = projectsOfClients.Where(x => x.Client == null).Any();
            if (hasClientsWithoutProjects)
            {
                var clientWithoutProjects = new Client
                {
                    Id = Constants.WithoutClient.Id,
                    Name = Constants.WithoutClient.Name,
                    IsActive = true,
                    Projects = projectsOfClients.Where(x => x.Client == null).ToList()
                };

                clientsFromProjectOfClients.Add(clientWithoutProjects);
            }

            #endregion

            var reportClientView = new List<ReportClientView>();

            foreach (var client in clientsFromProjectOfClients)
            {
                var reportProjectViewByUserId = new List<ReportProjectView>();

                foreach (var project in client.Projects)
                {
                    var reportProjectView = new ReportProjectView
                    {
                        ProjectId = project.Id,
                        ProjectName = project.Name,
                        RoleId = project.MemberProjectRoles.FirstOrDefault(r => r.MemberId == member.Id)?.RoleId ?? 0,
                        IsProjectActive = project.IsActive,
                    };

                    #region Set all users at Project constrain only for: Admin, Manager at this project.

                    var isManagerOnProject = project.MemberProjectRoles.Exists(r => r.MemberId == member.Id && r.RoleId == managerRoleId);

                    if (member.User.IsAdmin || isManagerOnProject)
                    {
                        var usersDetailsView = project.MemberProjectRoles.Select(x => x.Member.GetViewReportUsers(x.RoleId, Mapper)).ToList();

                        // Add members, that is not assigned to this project directly.
                        if (!project.IsPrivate)
                        {
                            var notAssignedMembersAtProjView = members.Where(x => project.MemberProjectRoles.Select(y => y.MemberId).All(mi => x.Id != mi)).Select(u => u.GetViewReportUsers(memberRoleId, Mapper));
                            usersDetailsView.AddRange(notAssignedMembersAtProjView);
                        }

                        // Set all users of the project.
                        reportProjectView.UsersDetails = usersDetailsView;
                    }

                    #endregion

                    reportProjectView.IsUserManagerOnProject = isManagerOnProject;
                    reportProjectViewByUserId.Add(reportProjectView);
                }

                var reportClientViewlocal = new ReportClientView
                {
                    ClientId = client.Id,
                    ClientName = client.Name,
                    IsClientActive = client.IsActive,
                    ProjectsDetails = reportProjectViewByUserId
                };

                reportClientView.Add(reportClientViewlocal);
            }

            var userDetails = new ReportUserDetails
            {
                CurrentUserFullName = member.FullName,
                CurrentUserId = member.Id,
                IsAdminCurrentUser = member.User.IsAdmin,
                IsManagerCurrentUser = member.User.IsManager,
            };

            var valuesCustomQueries = new List<ReportsSettingsView>();

            var customQueries = Uow.ReportsSettingsRepository.GetEntitiesFromContex_ByMemberid(member.Id).Where(x => x.QueryName != null);
            foreach (var customReportSettings in customQueries)
            {
                valuesCustomQueries.Add(CreateReportsSettingsEntity(customReportSettings));
            }

            var dropDownValues = new ReportDropDownValues
            {
                Filters = reportClientView,
                GroupBy = _dropDownGroupBy,
                ShowColumns = Constants.showColumnsInfo222,
                UserDetails = userDetails,
                CustomQueries = valuesCustomQueries.OrderBy(x => x.QueryName).ToList()
            };

            return dropDownValues;
        }

        private ReportsSettingsView CreateDropDownCurrentQuery(int memberId)
        {
            var reportsSettings = Uow.ReportsSettingsRepository.GetEntitiesFromContex_ByMemberid(memberId).FirstOrDefault(x => x.IsCurrentQuery);

            var dropDownsCurrentQueryList = CreateReportsSettingsEntity(reportsSettings);

            return dropDownsCurrentQueryList;
        }

        private ReportsSettingsView CreateReportsSettingsEntity(ReportsSettings defaultReportSettings)
        {
            var dropDownsQuery = new ReportsSettingsView
            {
                GroupById = SetGroupByOrDefaultGrouping(defaultReportSettings?.GroupById),
                ShowColumnIds = defaultReportSettings?.FilterShowColumnIds == null
                    ? new[]
                    {
                        (int) Constants.ShowColumnModelIds.ShowEstimatedTime,
                        (int) Constants.ShowColumnModelIds.ShowDate,
                        (int) Constants.ShowColumnModelIds.ShowNotes,
                        (int) Constants.ShowColumnModelIds.ShowStartFinish
                    }
                    : ConvertStringToArrayOfInts(defaultReportSettings.FilterShowColumnIds)
            };

            if (defaultReportSettings != null)
            {
                dropDownsQuery.DateFrom = defaultReportSettings.DateFrom;
                dropDownsQuery.DateTo = defaultReportSettings.DateTo;

                dropDownsQuery.ClientIds = ConvertStringToArrayOfNullableInts(defaultReportSettings.FilterClientIds);
                dropDownsQuery.ProjectIds = ConvertStringToArrayOfInts(defaultReportSettings.FilterProjectIds);
                dropDownsQuery.MemberIds = ConvertStringToArrayOfInts(defaultReportSettings.FilterMemberIds);
                dropDownsQuery.QueryName = defaultReportSettings.QueryName;
                dropDownsQuery.QueryId = defaultReportSettings.Id;
            }

            return dropDownsQuery;
        }

        private static int[] ConvertStringToArrayOfInts(string sourceString)
        {
            return !string.IsNullOrEmpty(sourceString)
                ? sourceString.Split(',').Select(int.Parse).ToArray()
                : null;
        }

        private static int?[] ConvertStringToArrayOfNullableInts(string sourceString)
        {
            return !string.IsNullOrEmpty(sourceString)
                ? sourceString.Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => (int?) Convert.ToInt32(x)).ToArray()
                : null;
        }
    }
}