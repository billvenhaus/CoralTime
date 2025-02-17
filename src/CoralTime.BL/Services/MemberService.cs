﻿using AutoMapper;
using CoralTime.BL.Helpers;
using CoralTime.BL.Interfaces;
using CoralTime.Common.Constants;
using CoralTime.Common.Exceptions;
using CoralTime.Common.Helpers;
using CoralTime.DAL.ConvertModelToView;
using CoralTime.DAL.ConvertViewToModel;
using CoralTime.DAL.Models;
using CoralTime.DAL.Models.Member;
using CoralTime.DAL.Repositories;
using CoralTime.ViewModels.Errors;
using CoralTime.ViewModels.Member;
using CoralTime.ViewModels.Member.MemberPasswordView;
using CoralTime.ViewModels.Notifications.ByWeeklyUpdates;
using CoralTime.ViewModels.Projects;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoralTime.Common.Constants.Constants;

namespace CoralTime.BL.Services
{
    public class MemberService : BaseService, IMemberService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly bool _isDemo;
        private readonly IImageService _avatarService;

        public MemberService(UnitOfWork uow, UserManager<ApplicationUser> userManager, IConfiguration configuration, IMapper mapper, IImageService avatarService)
            : base(uow, mapper)
        {
            _userManager = userManager;
            _configuration = configuration;
            _isDemo = bool.Parse(_configuration["DemoSiteMode"]);
            _avatarService = avatarService;
        }

        public IEnumerable<MemberView> GetAllMembers()
        {
            var globalActiveProjCount = Uow.ProjectRepository.LinkedCacheGetList().Where(x => !x.IsPrivate && x.IsActive).Select(x => x.Id).ToArray();

            var allMembers = GetAllMembersCommon();

            var allMembersView = allMembers.Select(p => p.GetViewWithGlobalProjectsCount(globalActiveProjCount, Mapper, _avatarService.GetUrlIcon(p.Id))).ToList();
            foreach (var member in allMembersView)
            {
                member.UrlIcon = _avatarService.GetUrlIcon(member.Id);
            }

            return allMembersView;
        }

        public MemberView GetById(int id)
        {
            var memberById = Uow.MemberRepository.LinkedCacheGetById(id);

            if (memberById == null)
            {
                throw new CoralTimeEntityNotFoundException($"Member with id {id} not found.");
            }

            var urlIcon = _avatarService.GetUrlIcon(memberById.Id);
            var memberViewResult = memberById.GetView(Mapper, urlIcon);

            return memberViewResult;
        }

        public IEnumerable<ProjectView> GetTimeTrackerAllProjects(int memberId)
        {
            return GetProjects(memberId).Select(p => p.GetViewTimeTrackerAllProjects(Mapper));
        }

        public async Task<MemberView> CreateNewUser(MemberView memberView, string baseUrl)
        {
            if (!EmailChecker.IsValidEmail(memberView.Email))
            {
                throw new CoralTimeDangerException("Invalid email");
            }

            var applicationUserNew = new ApplicationUser
            {
                UserName = memberView.UserName,
                Email = memberView.Email,
                IsActive = true,
                Role = memberView.Role
            };

            var roleUser = memberView.Role;

            #region Check ApplicationUser, Roles, Member

            // Check ApplicationUser
            var isExistApplicationUser = await _userManager.FindByNameAsync(memberView.UserName);
            if (isExistApplicationUser != null)
            {
                throw new CoralTimeAlreadyExistsException($"User with userName {memberView.UserName} already exist");
            }

            // Check ApplicationUser Roles
            var isExistRolesForMember = await _userManager.GetRolesAsync(applicationUserNew).ToAsyncEnumerable().AnyAsync(x => x.Contains(roleUser));
            if (isExistRolesForMember)
            {
                throw new CoralTimeAlreadyExistsException($"User with userName {memberView.UserName} already exist '{roleUser}' role");
            }

            // Check Member
            var isExistMember = Uow.MemberRepository.GetQueryByUserName(applicationUserNew.UserName);
            if (isExistMember != null)
            {
                throw new CoralTimeAlreadyExistsException($"Member with userName {memberView.UserName} already exist");
            }

            #endregion

            // Insert ApplicationUser
            var sb = new StringBuilder();
            sb.Append(Guid.NewGuid());
            sb.Append("A_");

            var userCreationResult = await _userManager.CreateAsync(applicationUserNew, sb.ToString());
            if (!userCreationResult.Succeeded)
            {
                CheckIdentityResultErrors(userCreationResult);
            }

            var applicationUser = await _userManager.FindByNameAsync(applicationUserNew.UserName);

            // Insert ApplicationUser Roles
            var userCreateRoleResult = await _userManager.AddToRoleAsync(applicationUser, roleUser);
            if (!userCreateRoleResult.Succeeded)
            {
                CheckIdentityResultErrors(userCreateRoleResult);
            }

            #region Set UserId to new Member. Save to Db. Get Member from Db with related entity User by UserId.

            // 1. Convert MemberView to Member.
            var newMember = memberView.GetModel(Mapper);

            // 2. Assign UserId to Member (After Save, when you try to get entity from Db, before assign UserId to entity then it has Related Entity User).
            newMember.UserId = applicationUser.Id;

            // 3. Save in Db.
            Uow.MemberRepository.Insert(newMember);
            Uow.Save();

            // 4. Clear cache for Members.
            Uow.MemberRepository.LinkedCacheClear();

            // 5. Get From Db -> Cache New Member. (Get entity With new created related entity - User)
            var memberByName = Uow.MemberRepository.LinkedCacheGetByName(memberView.UserName);

            #endregion

            // Identity #3. Create claims. Add Claims for user in AspNetUserClaims.
            var claimsUser = ClaimsCreator.CreateUserClaims(applicationUser.UserName, memberView.FullName, memberView.Email, roleUser, memberByName.Id);
            var claimsUserResult = await _userManager.AddClaimsAsync(applicationUser, claimsUser);
            if (!claimsUserResult.Succeeded)
            {
                CheckIdentityResultErrors(userCreateRoleResult);
            }

            var urlIcon = _avatarService.GetUrlIcon(memberByName.Id);
            var memberViewResult = memberByName.GetView(Mapper, urlIcon);

            await SentInvitationEmailForNewUserAsync(memberView, baseUrl, applicationUserNew);

            return memberViewResult;
        }

        public async Task<MemberView> Update(MemberView memberView, string baseUrl)
        {
            var currentMember = Uow.MemberRepository.GetQueryByMemberId(BaseMemberCurrent.Id);
            var updatedMember = Uow.MemberRepository.GetQueryByMemberId(memberView.Id);

            var canEditMember = Authorize(currentMember, PolicyEditMember);

            if (currentMember.Id != updatedMember.Id && !canEditMember)
            {
                throw new CoralTimeForbiddenException($"Member with userName {BaseMemberCurrent.User.UserName} can't change other user's data.");
            }

            if (! EmailChecker.IsValidEmail(memberView.Email))
            {
                throw new CoralTimeSafeEntityException("Invalid email");
            }

            if (_isDemo)
            {
                if (updatedMember.User.Email != memberView.Email)
                {
                    throw new CoralTimeForbiddenException("Email can't be changed on demo site");
                }

                if (updatedMember.User.UserName != memberView.UserName)
                {
                    throw new CoralTimeForbiddenException("Username can't be changed on demo site");
                }

                if (updatedMember.User.IsActive != memberView.IsActive)
                {
                    throw new CoralTimeForbiddenException("Status can't be changed on demo site");
                }

                if (updatedMember.FullName != memberView.FullName)
                {
                    throw new CoralTimeForbiddenException("Full name can't be changed on demo site");
                }
            }

            if (canEditMember)
            {
                var newEmail = memberView.Email;
                var newUserName = memberView.UserName;
                var newIsActive = memberView.IsActive;
                var newRole = memberView.Role;

                if (updatedMember.User.Email != newEmail || updatedMember.User.UserName != newUserName ||
                    updatedMember.User.IsActive != newIsActive || updatedMember.User.Role != newRole)
                {
                    updatedMember.User.Email = newEmail;
                    updatedMember.User.UserName = newUserName;

                    var updateResult = await _userManager.UpdateAsync(updatedMember.User);
                    if (updateResult.Succeeded)
                    {
                        var startRole = updatedMember.User.Role;

                        if (currentMember.Id != updatedMember.Id)
                        {
                            updatedMember.User.IsActive = newIsActive;
                            updatedMember.User.Role = newRole;
                        }

                        var finishRole = updatedMember.User.Role;

                        Uow.MemberRepository.Update(updatedMember);
                        Uow.Save();

                        if (startRole != finishRole)
                        {
                            await _userManager.RemoveFromRoleAsync(updatedMember.User, startRole);
                            await _userManager.AddToRoleAsync(updatedMember.User, finishRole);
                        }

                        UpdateUserClaims(updatedMember.Id);

                        Uow.MemberRepository.LinkedCacheClear();
                    }
                    else
                    {
                        CheckMembersErrors(updateResult.Errors.Select(e => new IdentityErrorView
                        {
                            Code = e.Code,
                            Description = e.Description
                        }));
                    }
                }
            }

            var memberById = Uow.MemberRepository.GetQueryByMemberId(updatedMember.Id);

            await ChangeEmailByUserAsync(memberById, memberView.Email);

            memberById.FullName = memberView.FullName;
            memberById.DefaultProjectId = memberView.DefaultProjectId;
            memberById.DefaultTaskId = memberView.DefaultTaskId;
            memberById.DateFormatId = memberView.DateFormatId;
            memberById.WeekStart = (WeekStart)memberView.WeekStart;
            memberById.IsWeeklyTimeEntryUpdatesSend =memberView.IsWeeklyTimeEntryUpdatesSend;
            memberById.TimeFormat = memberView.TimeFormat;
            memberById.SendEmailTime = memberView.SendEmailTime;
            memberById.SendEmailDays = ConverterBitMask.DayOfWeekStringToInt(memberView.SendEmailDays);
            memberById.WorkingHoursPerDay = memberView.WorkingHoursPerDay;

            try
            {
                Uow.MemberRepository.Update(memberById);

                if (Uow.Save() > 0)
                {
                    UpdateUserClaims(memberById.Id);
                }

                Uow.MemberRepository.LinkedCacheClear();
            }
            catch (Exception e)
            {
                Uow.MemberRepository.LinkedCacheClear();
                throw new CoralTimeDangerException("An error occurred while updating member", e);
            }

            var memberByIdResult = Uow.MemberRepository.LinkedCacheGetById(memberById.Id);
            var urlIcon = _avatarService.GetUrlIcon(memberByIdResult.Id);
            var updatedMemberView = memberByIdResult.GetView(Mapper, urlIcon);

            await SentUpdateAccountEmailAsync(updatedMemberView, baseUrl);

            return updatedMemberView;
        }

        public List<MemberWeeklyNotificationByDayOfWeekView> GetMembersWithWeeklyNotifications() => Uow.MemberRepository.LinkedCacheGetList()
            .Where(member => member.IsWeeklyTimeEntryUpdatesSend)
            .GroupBy(member => member.WeekStart.ToString())
            .Select(item => new MemberWeeklyNotificationByDayOfWeekView
            {
                WeekStartName = item.Key,
                Members = item.Select(member => new MemberWeeklyNotificationView
                {
                    Id = member.Id,
                    FullName = member.FullName
                }).ToList()
            }).ToList();

        #region Change Password.

        public async Task ChangePassword(MemberChangePasswordView member)
        {
            var user = await _userManager.FindByIdAsync(Uow.MemberRepository.GetById(member.Id).UserId);

            if (user == null)
            {
                throw new CoralTimeEntityNotFoundException($"user with id {Uow.MemberRepository.GetById(member.Id).UserId} not found.");
            }

            if (!user.IsActive)
            {
                throw new CoralTimeEntityNotFoundException($"user with id {Uow.MemberRepository.GetById(member.Id).UserId} is not active.");
            }

            if (_isDemo)
            {
                throw new CoralTimeForbiddenException($"Password can't be changed on demo site");
            }

            var userUpdationResult = await _userManager.ChangePasswordAsync(user, member.OldPassword, member.NewPassword);

            if (!userUpdationResult.Succeeded)
            {
                CheckMembersErrors(userUpdationResult.Errors.Select(e => new IdentityErrorView
                {
                    Code = e.Code,
                    Description = e.Description
                }));
            }
        }

        public async Task ResetPassword(int memberId)
        {
            var user = await _userManager.FindByIdAsync(Uow.MemberRepository.GetById(memberId).UserId);

            if (user == null)
            {
                throw new CoralTimeEntityNotFoundException($"user with id {Uow.MemberRepository.GetById(memberId).UserId} not found.");
            }

            if (!user.IsActive)
            {
                throw new CoralTimeEntityNotFoundException($"user with id {Uow.MemberRepository.GetById(memberId).UserId} is not active.");
            }

            var resetPasswordToken = await _userManager.GeneratePasswordResetTokenAsync(user);

            var userResetPassword = await _userManager.ResetPasswordAsync(user, resetPasswordToken, Guid.NewGuid().ToString());

            if (!userResetPassword.Succeeded)
            {
                CheckMembersErrors(userResetPassword.Errors.Select(e => new IdentityErrorView
                {
                    Code = e.Code,
                    Description = e.Description
                }));
            }
        }

        public async Task<ChangePasswordResultView> ChangePasswordByTokenAsync(MemberChangePasswordByTokenView model)
        {
            if (_isDemo)
            {
                throw new CoralTimeForbiddenException($"Password can't be changed on demo site");
            }
            
            var userForgotPassRequest = Uow.UserForgotPassRequestRepository.GetRequest(model.Token);

            if (userForgotPassRequest == null)
            {
                return new ChangePasswordResultView { IsChangedPassword = false, Message = (int)Constants.Errors.InvalidToken };
            }

            var user = await _userManager.FindByEmailAsync(userForgotPassRequest.Email);
            if (user == null)
            {
                return new ChangePasswordResultView { IsChangedPassword = false, Message = (int)Constants.Errors.EmailDoesntExist };
            }

            var result = await _userManager.ResetPasswordAsync(user, userForgotPassRequest.RefreshToken, model.NewPassword);

            if (result.Succeeded)
            {
                var tokenToDeleteIds = Uow.UserForgotPassRequestRepository.GetQuery()
                    .Where(x => x.Email == userForgotPassRequest.Email)
                    .Select(y => y.Id)
                    .ToList();

                tokenToDeleteIds.ForEach(id => Uow.UserForgotPassRequestRepository.Delete(id));
                Uow.Save(isAnonymousRequest: true);

                return new ChangePasswordResultView { IsChangedPassword = true, Message = (int)Constants.Errors.None };
            }

            var errors = string.Empty;
            foreach (var error in result.Errors)
            {
                errors += $"{error.Description} ";
            }
            var errorResult = (int)((errors.Contains("token")) ? Constants.Errors.InvalidToken : Constants.Errors.ErrorPassword);
            return new ChangePasswordResultView { IsChangedPassword = false, Message = errorResult };
        }

        #endregion

        #region Emails: Invitation, Update Account, Forgot, Check Forgot Password Token, Change Email.

        private async Task SentInvitationEmailForNewUserAsync(MemberView member, string baseUrl, ApplicationUser user)
        {
            if (_isDemo)
            {
                return;
            }

            var sb = new StringBuilder();

            sb.Append($"Dear {member.FullName},<br/><br/> You have been invited to join CoralTime time tracking tool.<br/><br/>");

            bool.TryParse(_configuration["Authentication:EnableAzure"], out bool enableAzure);
            if (!enableAzure)
            {
                sb.Append($"Below are your login details:<br/><br/> Username: {member.UserName}<br/><br/>");

                var passwordResetLinkValidForHrs = int.Parse(_configuration["PasswordResetLinkValidForHrs"]);
                var userForgotPassRequest = await CreateUserForgotPassRequest(member.Email, user, passwordResetLinkValidForHrs);

                var link = CreateLinkByUserForgotPassRequestUid(baseUrl, userForgotPassRequest);

                sb.Append($"To set your password, click this link or copy the URL below and paste it into your web browser's navigation bar:<br /><a href='{link}'>{link}</a><br /><br />Please note: This link will expire in {passwordResetLinkValidForHrs} hours. If it has already expired, please go back to <a href='{baseUrl}'>{baseUrl}</a> and click the \"Set Password?\"<br /><br />");

                var profileUrl = baseUrl + "/profile/settings";
                sb.Append($"You can change your password at any time on your <a href='{profileUrl}'>Profile page</a>.<br/><br/>");
            }

            sb.Append($"To get started, please click the link: <a href='{baseUrl}'>CoralTime</a><br/>If the link doesn’t work, copy and past URL into your browser.<br/><br/>Best wishes, CoralTime Team");

            var body = new TextPart("html") {Text = sb.ToString()};

            var multipart = new Multipart { body };

            var emailSender = new EmailSender(_configuration);

            emailSender.CreateSimpleMessage(member.Email, multipart, "Invitation to join CoralTime");

            await emailSender.SendMessageAsync();
        }

        public async Task SentUpdateAccountEmailAsync(MemberView member, string baseUrl)
        {
            if (_isDemo)
            {
                return;
            }
            
            var profileUrl = baseUrl + "/profile/settings";

            var sb = new StringBuilder();
            sb.Append($"Dear {member.FullName},<br/><br/>");
            sb.Append("Your account data have been changed in CoralTime time tracking tool.<br/><br/>");
            sb.Append($"You can change your account data at any time on your <a href='{profileUrl}'>Profile page</a>.<br/><br/>");
            sb.Append("If the link doesn’t work, copy and past URL into your browser.<br/><br/>");
            sb.Append("Best wishes,<br/>CoralTime Team");

            var body = new TextPart("html") {Text = sb.ToString()};

            var multipart = new Multipart { body };

            var emailSender = new EmailSender(_configuration);

            emailSender.CreateSimpleMessage(member.Email, multipart, "Your account data have been change in CoralTime");
            await emailSender.SendMessageAsync();
        }

        public async Task<PasswordForgotEmailResultView> SentForgotEmailAsync(string email, string serverUrl)
        {
            if (_isDemo)
            {
                return new PasswordForgotEmailResultView { IsSentEmail = false, Message = (int)Constants.Errors.ErrorSendEmail };
            }
            
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return new PasswordForgotEmailResultView { IsSentEmail = false, Message = (int)Constants.Errors.EmailDoesntExist };
            }

            if (!user.IsActive)
            {
                return new PasswordForgotEmailResultView { IsSentEmail = false, Message = (int)Constants.Errors.UserIsArchived };
            }

            var passwordResetLinkValidForHrs = int.Parse(_configuration["PasswordResetLinkValidForHrs"]);
            var userForgotPassRequest = await CreateUserForgotPassRequest(email, user, passwordResetLinkValidForHrs);

            if (userForgotPassRequest == null)
            {
                return new PasswordForgotEmailResultView { IsSentEmail = false, Message = (int)Constants.Errors.ErrorSendEmail };
            }

            var link = CreateLinkByUserForgotPassRequestUid(serverUrl, userForgotPassRequest);

            var sb = new StringBuilder().Append("CoralTime received a request to reset your password.<br /><br />");
            sb.Append($"To reset your password, click this link or copy the URL below and paste it into your web browser's navigation bar:<br />{link}<br /><br />Please note: This link will expire in {passwordResetLinkValidForHrs} hours. If it has already expired, please go back to {serverUrl} and click the \"Set Password?\"<br /><br />");
            sb.Append("Best wishes, the CoralTime team");

            var body = new TextPart("html")
            {
                Text = sb.ToString()
            };

            var emailSender = new EmailSender(_configuration);

            var subject = "Reset your CoralTime password.";
            emailSender.CreateSimpleMessage(email, new Multipart { body }, subject);

            try
            {
                await emailSender.SendMessageAsync();

                return new PasswordForgotEmailResultView { IsSentEmail = true, Message = (int)Constants.Errors.None };
            }
            catch (Exception)
            {
                return new PasswordForgotEmailResultView { IsSentEmail = false, Message = (int)Constants.Errors.ErrorSendEmail };
            }
        }

        private string CreateLinkByUserForgotPassRequestUid(string serverUrl, UserForgotPassRequest userForgotPassRequest)
        {
            var userForgotPassRequestUid = userForgotPassRequest.UserForgotPassRequestUid;
            var link = $"{serverUrl}{UrlSetPassword}/enter-new-password?restoreCode={userForgotPassRequestUid}";
            
            return link;
        }

        private async Task<UserForgotPassRequest> CreateUserForgotPassRequest(string email, ApplicationUser user, int passwordResetLinkValidForHrs)
        {
            var token = await GetForgotToken(user);
            var userForgotPassRequest = Uow.UserForgotPassRequestRepository.CreateUserForgotPassRequest(email, passwordResetLinkValidForHrs,token);

            return userForgotPassRequest;
        }

        public async Task<CheckForgotPasswordTokenResultView> CheckForgotPasswordTokenAsync(string token)
        {
            var userForgotPassRequest = Uow.UserForgotPassRequestRepository.GetRequest(token);

            if (userForgotPassRequest == null)
            {
                return new CheckForgotPasswordTokenResultView { IsTokenValid = false, Message = (int)Constants.Errors.InvalidToken };
            }

            var user = await _userManager.FindByEmailAsync(userForgotPassRequest.Email);
            if (user == null)
            {
                return new CheckForgotPasswordTokenResultView { IsTokenValid = false, Message = (int)Constants.Errors.EmailDoesntExist };
            }

            if (!user.IsActive)
            {
                return new CheckForgotPasswordTokenResultView { IsTokenValid = false, Message = (int)Constants.Errors.EmailDoesntExist };
            }

            return new CheckForgotPasswordTokenResultView { IsTokenValid = true, Message = (int)Constants.Errors.None };
        }

        public async Task ChangeEmailByUserAsync(Member member, string newEmail)
        {
            if (_isDemo)
            {
                throw new CoralTimeForbiddenException($"Email can't be changed on demo site");
            }
            
            if (!string.IsNullOrWhiteSpace(newEmail) && member.User.Email != newEmail)
            {
                member.User.Email = newEmail;
                var updateEmailResult = await _userManager.UpdateAsync(member.User);

                if (!updateEmailResult.Succeeded)
                {
                    CheckMembersErrors(updateEmailResult.Errors.Select(e => new IdentityErrorView
                    {
                        Code = e.Code,
                        Description = e.Description
                    }));
                }
            }
        }

        #endregion

        #region Claims & Token.

        public void UpdateUsersClaims()
        {
            var memberIds = Uow.MemberRepository.LinkedCacheGetList().Select(m => m.Id).ToList();

            foreach (var memberId in memberIds)
            {
                try
                {
                    UpdateUserClaims(memberId);
                    Uow.MemberRepository.LinkedCacheClear();
                }
                catch (Exception e)
                {
                    Uow.MemberRepository.LinkedCacheClear();
                    throw new CoralTimeDangerException($"Error update member claims by MemberId = {memberId}.", e);
                }
            }
        }

        public void UpdateUserClaims(int memberId)
        {
            var memberById = Uow.MemberRepository.GetQuery().FirstOrDefault(m => m.Id == memberId);

            #region v1.

            //try
            //{
            //    memberById.User.Claims.Clear();
            //    Uow.MemberRepository.Update(memberById);
            //    Uow.Save();

            //    // Assigns claims.
            //    var claims = ClaimsCreator.GetUserClaims(memberById.User.UserName, memberById.FullName, memberById.User.Email, memberById.User.IsAdmin ? AdminRole : UserRole, memberId);

            //    _userManager.AddClaimsAsync(memberById.User, claims).GetAwaiter().GetResult();

            //    ClearCacheMembers();
            //}

            //catch (Exception) { }

            #endregion

            #region v2.

            //memberByName.User.Claims.Clear();
            //memberByName.User.Claims.Add(new IdentityUserClaim<string> { ClaimType = JwtClaimTypes.Email, ClaimValue = memberByName.User.Email, });
            //var updatememberByNameClaims = _userManager.UpdateAsync(memberByName.User).GetAwaiter().GetResult();
            //if (!updatememberByNameClaims.Succeeded)
            //{
            //    BLHelpers.CheckMembersErrors(updatememberByNameClaims.Errors.Select(e => new IdentityErrorView
            //    {
            //        Code = e.Code,
            //        Description = e.Description
            //    }));
            //}

            #endregion

            var memberByNameClaims = _userManager.GetClaimsAsync(memberById.User).GetAwaiter().GetResult();
            var removeClaimsForMember = _userManager.RemoveClaimsAsync(memberById.User, memberByNameClaims).GetAwaiter().GetResult();
            if (!removeClaimsForMember.Succeeded)
            {
                CheckIdentityResultErrors(removeClaimsForMember);
            }

            var claimsForMemberNew = ClaimsCreator.CreateUserClaims(memberById.User.UserName, memberById.FullName, memberById.User.Email, memberById.User.Role, memberById.Id);
            var addClaimsForMember = _userManager.AddClaimsAsync(memberById.User, claimsForMemberNew).GetAwaiter().GetResult();
            if (!addClaimsForMember.Succeeded)
            {
                CheckIdentityResultErrors(addClaimsForMember);
            }
        }

        private async Task<string> GetForgotToken(ApplicationUser user)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            return token;
        }

        #endregion

        #region Other Methods.

        private void CheckIdentityResultErrors(IdentityResult userCreateRoleResult)
        {
            CheckMembersErrors(userCreateRoleResult.Errors.Select(e => new IdentityErrorView
            {
                Code = e.Code,
                Description = e.Description
            }));
        }

        private void CheckMembersErrors(IEnumerable<IdentityErrorView> result)
        {
            var passwordErrors = new List<ErrorODataView>();
            var otherException = new List<ErrorODataView>();

            foreach (var error in result)
            {
                if (error.Code.Contains("Password"))
                {
                    passwordErrors.Add(new ErrorODataView
                    {
                        Source = "Password",
                        Title = StringHandler.SeparateStringByUpperCase(error.Code),
                        Details = error.Description
                    });
                }
                else if (error.Code.Contains("UserName"))
                {
                    otherException.Add(new ErrorODataView
                    {
                        Source = "UserName",
                        Title = StringHandler.SeparateStringByUpperCase(error.Code),
                        Details = error.Description
                    });
                }
                else
                {
                    otherException.Add(new ErrorODataView
                    {
                        Source = "Other",
                        Title = StringHandler.SeparateStringByUpperCase(error.Code),
                        Details = error.Description
                    });
                }
            }
            if (passwordErrors.Count > 0)
            {
                throw new CoralTimeIncorrectPasswordException
                {
                    errors = passwordErrors
                };
            }
            if (otherException.Count > 0)
            {
                throw new CoralTimeSafeEntityException
                {
                    errors = otherException
                };
            }
        }

        private IEnumerable<Member> GetAllMembersCommon()
        {
            var viewMember = Authorize(BaseMemberImpersonated, PolicyViewMember);
            if (viewMember)
            {
                var membersAll = Uow.MemberRepository.LinkedCacheGetList();
                if (membersAll == null)
                {
                    throw new CoralTimeEntityNotFoundException($"Members not found.");
                }

                return membersAll;
            }

            var memberById = Uow.MemberRepository.LinkedCacheGetByUserName(BaseMemberImpersonated.User.UserName);
            if (memberById == null)
            {
                throw new CoralTimeEntityNotFoundException($"Member by user id {BaseMemberImpersonated.User.Id} not found.");
            }

            return new List<Member> { memberById };
        }

        public IEnumerable<Project> GetProjects(int memberId)
        {
            var memberById = Uow.MemberRepository.GetById(memberId);
            if (memberById == null)
            {
                throw new CoralTimeEntityNotFoundException($"Member with id {memberId} not found.");
            }

            var memberProjectsRoles = Uow.MemberProjectRoleRepository.LinkedCacheGetList()
                .Where(r => r.MemberId == memberId)
                .ToList();

            var projects = memberProjectsRoles.Select(mpr => new Project
            {
                ClientId = mpr.Project.ClientId,
                Color = mpr.Project.Color,
                DaysBeforeStopEditTimeEntries = mpr.Project.DaysBeforeStopEditTimeEntries,
                Description = mpr.Project.Description,
                Id = mpr.Project.Id,
                IsActive = mpr.Project.IsActive,
                IsPrivate = mpr.Project.IsPrivate,
                IsTimeLockEnabled = mpr.Project.IsTimeLockEnabled,
                LockPeriod = mpr.Project.LockPeriod,
                Name = mpr.Project.Name,
                TaskTypes = Uow.TaskTypeRepository.LinkedCacheGetList().Where(t => (t.ProjectId == null || t.ProjectId == mpr.ProjectId) && t.IsActive).ToList(),
                MemberProjectRoles = Uow.MemberProjectRoleRepository.LinkedCacheGetList().Where(mr => mr.ProjectId == mpr.ProjectId).ToList()
            });

            return projects;
        }

        #endregion
    }
}