// WorkspaceService.cs
using Final_Project_Backend.Data;
using Final_Project_Backend.Models;
using Final_Project_Backend.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
// using YourProjectNamespace.Models;

namespace Final_Project_Backend.Services
{
    public class WorkspaceService : IWorkspaceService
    {
        private readonly AppDbContext _context;
        private readonly IUserRepository _userRepository;
        private readonly INotificationService _notificationService;

        public WorkspaceService(AppDbContext context, IUserRepository userRepository, INotificationService notificationService)
        {
            _context = context;
            _userRepository = userRepository;
            _notificationService = notificationService;
        }

        public async Task<WorkspaceResponseDto?> GetWorkspaceById(int userId, int workspaceId)
        {
            var workspace = await _context.Workspaces
                .Include(w => w.UserWorkspaces)
                .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId);

            if (workspace == null)
            {
                return null;
            }

            var userWorkspace = workspace.UserWorkspaces
                .FirstOrDefault(uw => uw.UserId == userId);

            if (userWorkspace == null)
            {
                return null;
            }

            return new WorkspaceResponseDto(
                workspace.WorkspaceId,
                workspace.Name,
                workspace.Description,
                workspace.CreatedByUserId,
                userWorkspace.Role.ToString()
            );
        }

        public async Task<IEnumerable<Workspace>> GetWorkspacesByUser(int userId)
        {
            return await _context.UserWorkspaces
                .Where(wu => wu.UserId == userId)
                .Include(wu => wu.Workspace)
                .ThenInclude(w => w.Projects)
                .Select(wu => wu.Workspace)
                .ToListAsync();
        }

        public async Task<IEnumerable<WorkspaceResponseDto>> GetWorkspaceDtosByUser(int userId)
        {
            Console.WriteLine($"Getting workspaces for user ID: {userId}");

            var workspaces = await _context.UserWorkspaces
                .Where(wu => wu.UserId == userId)
                .Select(wu => new WorkspaceResponseDto(
                    wu.Workspace.WorkspaceId,
                    wu.Workspace.Name,
                    wu.Workspace.Description,
                    wu.Workspace.CreatedByUserId,
                    wu.Role.ToString()
                ))
                .ToListAsync();

            Console.WriteLine($"Found {workspaces.Count} workspaces");

            return workspaces;
        }

        public async Task<WorkspaceResponseDto> CreateWorkspace(int userId, WorkspaceCreateDto workspaceDto)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var workspace = new Workspace
                {
                    Name = workspaceDto.Name,
                    Description = workspaceDto.Description,
                    CreatedByUserId = userId
                };

                _context.Workspaces.Add(workspace);
                await _context.SaveChangesAsync();

                var userWorkspace = new UserWorkspace
                {
                    WorkspaceId = workspace.WorkspaceId,
                    UserId = userId,
                    Role = WorkspaceRole.Admin,
                    // JoinedAt = DateTime.UtcNow,
                    User = user
                };

                _context.UserWorkspaces.Add(userWorkspace);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return new WorkspaceResponseDto(
                    workspace.WorkspaceId,
                    workspace.Name,
                    workspace.Description,
                    workspace.CreatedByUserId,
                    userWorkspace.Role.ToString()
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating workspace: {ex.Message}");
                await transaction.RollbackAsync();
                throw new Exception("Error creating workspace and user workspace");
            }
        }

        public async Task<bool> AddUserToWorkspace(int requestingUserId, int workspaceId, AddUserToWorkspaceDto dto)
        {
            var requestingUserRole = await _context.UserWorkspaces
                .FirstOrDefaultAsync(wu => wu.WorkspaceId == workspaceId && wu.UserId == requestingUserId);

            if (requestingUserRole == null || requestingUserRole.Role != WorkspaceRole.Admin)
            {
                return false;
            }

            var workspace = await _context.Workspaces
                .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId);

            if (workspace == null)
            {
                throw new KeyNotFoundException("Workspace not found");
            }

            var userToAdd = await _userRepository.GetUserByEmailAsync(dto.Email);
            if (userToAdd == null)
            {
                return false;
            }

            var existingMembership = await _context.UserWorkspaces
                .FirstOrDefaultAsync(wu => wu.WorkspaceId == workspaceId && wu.UserId == userToAdd.UserId);

            if (existingMembership != null)
            {
                return false;
            }

            var userWorkspace = new UserWorkspace
            {
                WorkspaceId = workspaceId,
                UserId = userToAdd.UserId,
                Role = dto.Role,
                JoinedAt = DateTime.UtcNow,
                User = userToAdd
            };

            _context.UserWorkspaces.Add(userWorkspace);
            await _context.SaveChangesAsync();

            await _notificationService.CreateNotification(
                userToAdd.UserId,
                NotificationType.USER_ADDED_TO_WORKSPACE,
                $"You have been added to the workspace '{workspace.Name}'."
            );

            return true;
        }

        public async Task<IEnumerable<UserWithRoleDto>> GetUserWorkspaces(int workspaceId)
        {
            return await _context.UserWorkspaces
                .Where(wu => wu.WorkspaceId == workspaceId)
                .Include(wu => wu.User)
                .Select(wu => new UserWithRoleDto
                {
                    UserId = wu.User.UserId,
                    FullName = wu.User.FullName,
                    Email = wu.User.Email,
                    Role = wu.Role.ToString()
                })
                .ToListAsync();
        }

        public async Task<bool> RemoveUserFromWorkspace(int requestingUserId, int workspaceId, int userIdToRemove)
        {
            var requestingUserRole = await _context.UserWorkspaces
                .FirstOrDefaultAsync(uw => uw.WorkspaceId == workspaceId && uw.UserId == requestingUserId);

            if (requestingUserRole?.Role != WorkspaceRole.Admin)
                return false;

            var userWorkspace = await _context.UserWorkspaces
                .FirstOrDefaultAsync(uw => uw.WorkspaceId == workspaceId && uw.UserId == userIdToRemove);

            if (userWorkspace == null) return false;

            _context.UserWorkspaces.Remove(userWorkspace);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Workspace?> UpdateWorkspace(int userId, int workspaceId, WorkspaceUpdateDto dto)
        {
            var workspace = await _context.Workspaces
                .Include(w => w.UserWorkspaces)
                .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId);

            if (workspace?.UserWorkspaces.FirstOrDefault(uw => uw.UserId == userId)?.Role != WorkspaceRole.Admin)
                return null;

            workspace.Name = dto.Name ?? workspace.Name;
            workspace.Description = dto.Description ?? workspace.Description;

            await _context.SaveChangesAsync();
            return workspace;
        }

        public async Task<bool> DeleteWorkspace(int userId, int workspaceId)
        {
            var workspace = await _context.Workspaces
                .Include(w => w.UserWorkspaces)
                .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId);

            if (workspace?.UserWorkspaces.FirstOrDefault(uw => uw.UserId == userId)?.Role != WorkspaceRole.Admin)
                return false;

            _context.Workspaces.Remove(workspace);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Dictionary<WorkspaceRole, int>> CountWorkspacesByRole(int userId)
        {
            var roleCounts = await _context.UserWorkspaces
                .Where(uw => uw.UserId == userId)
                .GroupBy(uw => uw.Role)
                .Select(group => new
                {
                    Role = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(g => g.Role, g => g.Count);

            return roleCounts;
        }

        public async Task<Tag?> CreateTag(int userId, int workspaceId, CreateTagDto dto)
        {
            // Check if user has Admin/Member role in the workspace
            var userWorkspace = await _context.UserWorkspaces
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WorkspaceId == workspaceId);

            // Deny if user is not in workspace or is a Viewer
            if (userWorkspace == null || userWorkspace.Role == WorkspaceRole.Viewer)
                return null;

            var tag = new Tag
            {
                Name = dto.Name,
                Color = dto.Color,
                CreatedByUserId = userId,
                WorkspaceId = workspaceId,
                CreatedByUser = userWorkspace.User,
                Workspace = userWorkspace.Workspace
            };

            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();
            return tag;
        }

        public async Task<IEnumerable<Tag>> GetTagsByWorkspace(int userId, int workspaceId)
        {
            // Check if user has access to the workspace
            var userWorkspace = await _context.UserWorkspaces
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WorkspaceId == workspaceId);

            if (userWorkspace == null)
            {
                throw new UnauthorizedAccessException("User does not have access to this workspace");
            }

            // Retrieve the tags for the workspace
            return await _context.Tags
                .Where(t => t.WorkspaceId == workspaceId)
                .ToListAsync();
        }

        public async Task<bool> AssignTagToTask(int userId, int taskId, int tagId)
        {
            // Get task and include its project/workspace
            var task = await _context.Tasks
                .Include(t => t.Project)
                .ThenInclude(p => p.Workspace)
                .FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (task == null)
                return false;

            // Check if user has Admin/Member access to the workspace
            bool hasPermission = await _context.UserWorkspaces
                .AnyAsync(uw => uw.UserId == userId &&
                               uw.WorkspaceId == task.Project.WorkspaceId &&
                               uw.Role != WorkspaceRole.Viewer);

            if (!hasPermission)
                return false;

            // Verify tag exists in the same workspace
            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.TagId == tagId && t.WorkspaceId == task.Project.WorkspaceId);

            if (tag == null)
                return false;

            // Assign tag
            _context.TaskTags.Add(new TaskTag { TaskId = taskId, TagId = tagId });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> HasAccessToTaskWorkspace(int userId, int taskId)
        {
            // Get the task and include its project and workspace
            var task = await _context.Tasks
                .Include(t => t.Project)
                .ThenInclude(p => p.Workspace)
                .FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (task == null)
                return false;

            // Check if the user is part of the workspace
            var isUserInWorkspace = await _context.UserWorkspaces
                .AnyAsync(uw => uw.WorkspaceId == task.Project.Workspace.WorkspaceId && uw.UserId == userId);

            return isUserInWorkspace;
        }

        public async Task<IEnumerable<UserSearchDto>> SearchUsers(string query)
        {
            return await _context.Users
                .Where(u => u.FullName.Contains(query) || u.Email.Contains(query))
                .Select(u => new UserSearchDto
                {
                    Id = u.UserId,
                    Email = u.Email,
                    FullName = u.FullName
                })
                .ToListAsync();
        }

        public async Task<bool> IsWorkspaceAdmin(int userId, int workspaceId)
        {
            var userWorkspace = await _context.UserWorkspaces
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WorkspaceId == workspaceId);
            return userWorkspace?.Role == WorkspaceRole.Admin;
        }
    }
}