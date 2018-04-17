﻿using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.API.Commands;
using Rocket.API.Configuration;
using Rocket.API.Permissions;
using Rocket.Core.ServiceProxies;

namespace Rocket.Core.Permissions
{
    [ServicePriority(Priority = ServicePriority.Lowest)]
    public class ConfigurationPermissionProvider : IPermissionProvider
    {
        public IConfigurationElement GroupsConfig { get; protected set; }
        public IConfigurationElement PlayersConfig { get; protected set; }

        public bool SupportsPermissible(IPermissible permissible)
        {
            return permissible is IPermissionGroup || permissible is ICommandCaller;
        }

        public PermissionResult HasPermission(IPermissible target, string permission)
        {
            GuardLoaded();
            GuardPermission(ref permission);
            GuardPermissible(target);

            if (!permission.StartsWith("!") && HasPermission(target, "!" + permission) == PermissionResult.Grant)
                return PermissionResult.Deny;

            var permissionTree = BuildPermissionTree(permission);
            foreach (var permissionNode in permissionTree)
            {
                string[] groupPermissions = GetConfigSection(target)["Permissions"].Get(new string[0]);
                if (groupPermissions.Any(c => c.Trim().Equals(permissionNode, StringComparison.OrdinalIgnoreCase)))
                    return PermissionResult.Grant;
            }

            // check parent group permissions / player group permissions
            IEnumerable<IPermissionGroup> groups = GetGroups(target);
            foreach (var group in groups)
            {
                var result = HasPermission(group, permission);
                if (result == PermissionResult.Grant)
                    return PermissionResult.Grant;

                if (result == PermissionResult.Deny)
                    return PermissionResult.Deny;
            }

            return PermissionResult.Default;
        }

        public PermissionResult HasAllPermissions(IPermissible target, params string[] permissions)
        {
            GuardLoaded();
            GuardPermissions(permissions);
            GuardPermissible(target);

            PermissionResult result = PermissionResult.Grant;

            foreach (var permission in permissions)
            {
                var tmp = HasPermission(target, permission);
                if (tmp == PermissionResult.Deny)
                    return PermissionResult.Deny;

                if (tmp == PermissionResult.Default)
                    result = PermissionResult.Default;
            }

            return result;
        }

        public PermissionResult HasAnyPermissions(IPermissible target, params string[] permissions)
        {
            GuardLoaded();
            GuardPermissions(permissions);
            GuardPermissible(target);

            foreach (var permission in permissions)
            {
                var result = HasPermission(target, permission);
                if (result == PermissionResult.Deny)
                    return PermissionResult.Deny;

                if (result == PermissionResult.Grant)
                    return PermissionResult.Grant;
            }

            return PermissionResult.Default;
        }

        public bool AddPermission(IPermissible target, string permission)
        {
            GuardPermission(ref permission);
            GuardPermissible(target);

            var permsSection = GetConfigSection(target)["Permissions"];
            List<string> groupPermissions = permsSection.Get(defaultValue: new string[0]).ToList();
            groupPermissions.Add(permission);
            permsSection.Set(groupPermissions.ToArray());
            return true;
        }

        public bool AddDeniedPermission(IPermissible target, string permission)
        {
            GuardPermission(ref permission);
            GuardPermissible(target);

            return AddPermission(target, "!" + permission);
        }

        public bool AddPermission(ICommandCaller caller, string permission)
        {
            GuardPermission(ref permission);
            GuardPermissible(caller);

            var permsSection = GetConfigSection(caller)["Permissions"];
            List<string> groupPermissions = permsSection.Get(defaultValue: new string[0]).ToList();
            groupPermissions.Add(permission);
            permsSection.Set(groupPermissions.ToArray());
            return true;
        }

        public bool RemovePermission(IPermissible target, string permission)
        {
            GuardPermission(ref permission);
            var permsSection = GetConfigSection(target)["Permissions"];
            List<string> groupPermissions = permsSection.Get(defaultValue: new string[0]).ToList();
            int i = groupPermissions.RemoveAll(c => c.Trim().Equals(permission, StringComparison.OrdinalIgnoreCase));
            permsSection.Set(groupPermissions.ToArray());
            return i > 0;
        }

        public bool RemoveDeniedPermission(IPermissible target, string permission)
        {
            GuardPermission(ref permission);
            GuardPermissible(target);

            return RemovePermission(target, "!" + permission);
        }


        public IPermissionGroup GetPrimaryGroup(ICommandCaller caller)
        {
            GuardLoaded();
            return GetGroups(caller).OrderByDescending(c => c.Priority).FirstOrDefault();
        }

        public IEnumerable<IPermissionGroup> GetGroups(IPermissible target)
        {
            GuardLoaded();
            GuardPermissible(target);

            IConfigurationSection groupsSection = GetGroupsSection(target);
            string[] groups = groupsSection.Get(defaultValue: new string[0]);
            return groups
                   .Select(GetGroup)
                   .Where(c => c != null);
        }

        public IPermissionGroup GetGroup(string id)
        {
            return GetGroups().FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IPermissionGroup> GetGroups()
        {
            GuardLoaded();
            List<IPermissionGroup> groups = new List<IPermissionGroup>();
            foreach (IConfigurationSection child in GroupsConfig)
            {
                PermissionGroup group = new PermissionGroup();
                group.Id = child.Key;
                group.Name = child["Name"].Get(defaultValue: child.Key);
                group.Priority = child.Get(0);
                groups.Add(group);
            }

            return groups;
        }

        public void UpdateGroup(IPermissionGroup @group)
        {
            GuardLoaded();
            GuardPermissible(group);

            if (GetGroup(@group.Id) == null)
                throw new Exception("Can't update group that does not exist: " + @group.Id);

            IConfigurationSection section = GroupsConfig.GetSection($"{@group.Id}");

            if (!section.ChildExists("Name"))
                section.CreateSection("Name", SectionType.Value);
            section["Name"].Set(@group.Name);

            if (!section.ChildExists("Priority"))
                section.CreateSection("Priority", SectionType.Value);
            section["Priority"].Set(@group.Priority);
        }

        public bool AddGroup(IPermissible target, IPermissionGroup @group)
        {
            GuardLoaded();
            GuardPermissible(target);
            GuardPermissible(group);

            IConfigurationSection groupsSection = GetGroupsSection(target);
            List<string> groups = groupsSection.Get(defaultValue: new string[0]).ToList();
            if (!groups.Any(c => c.Equals(@group.Id, StringComparison.OrdinalIgnoreCase)))
                groups.Add(@group.Id);
            groupsSection.Set(groups.ToArray());
            return true;
        }

        public bool RemoveGroup(IPermissible target, IPermissionGroup @group)
        {
            GuardLoaded();
            GuardPermissible(target);
            GuardPermissible(group);
            
            IConfigurationSection groupsSection = GetGroupsSection(target);
            List<string> groups = groupsSection.Get(defaultValue: new string[0]).ToList();
            int i = groups.RemoveAll(c => c.Equals(@group.Id, StringComparison.OrdinalIgnoreCase));
            groupsSection.Set(groups.ToArray());
            return i > 0;
        }

        public bool CreateGroup(IPermissionGroup @group)
        {
            GuardLoaded();
            GuardPermissible(group);

            IConfigurationSection section = GroupsConfig.CreateSection($"{@group.Id}", SectionType.Object);
            section.CreateSection("Name", SectionType.Value).Set(@group.Name);
            section.CreateSection("Priority", SectionType.Value).Set(@group.Priority);
            return true;
        }

        public bool DeleteGroup(IPermissionGroup @group)
        {
            GuardLoaded();
            GuardPermissible(group);

            return GroupsConfig.RemoveSection($"{@group.Id}");
        }

        public void Load(IConfigurationElement groupsConfig, IConfigurationElement playersConfig)
        {
            GroupsConfig = groupsConfig;
            PlayersConfig = playersConfig;
        }

        public void Reload()
        {
            GroupsConfig.Root?.Reload();
            PlayersConfig.Root?.Reload();
        }

        public void Save()
        {
            GroupsConfig.Root?.Save();
            PlayersConfig.Root?.Save();
        }

        /// <summary>
        /// Builds a parent permission tree for the given permission <br/>
        /// If the target has any of these permissions, they will automatically have the given permission too <br/><br/> 
        /// <b>Example Input:</b>
        /// <code>
        /// "player.test.sub"
        /// </code>
        /// <b>Example output:</b>
        /// <code>
        /// [
        ///     "*",
        ///     "player.*",
        ///     "player.test.*",
        ///     "player.test.sub"
        /// ]
        /// </code>
        /// </summary>
        /// <param name="permission">The permission to build the tree for</param>
        /// <returns>The collection of all parent permission nodes</returns>
        public IEnumerable<string> BuildPermissionTree(string permission)
        {
            List<string> permissions = new List<string>
            {
                "*"
            };

            string parentPath = "";
            foreach (var childPath in permission.Split('.'))
            {
                permissions.Add(parentPath + childPath + ".*");
                parentPath += childPath + ".";
            }

            //remove last element because it should not contain "<permission>.*"
            //If someone has "permission.x.*" they should not have "permission.x" too
            permissions.RemoveAt(permissions.Count - 1);

            permissions.Add(permission);
            return permissions;
        }

        private void GuardPermission(ref string permission)
        {
            if (string.IsNullOrEmpty(permission))
                throw new ArgumentException("Argument can not be null or empty", nameof(permission));

            //permission = permission.ToLower().Trim();
            permission = permission.Trim();
        }

        private void GuardPermissions(string[] permissions)
        {
            for (int i = 0; i < permissions.Length; i++)
            {
                var tmp = permissions[i];
                GuardPermission(ref tmp);
                permissions[i] = tmp;
            }
        }

        private IConfigurationSection GetConfigSection(IPermissible target)
        {
            GuardPermissible(target);

            var config = target is IPermissionGroup ? GroupsConfig : PlayersConfig;

            var basePath = target is IPermissionGroup ? $"{target.Id}" : $"{((ICommandCaller)target).CallerType.Name}.{target.Id}";
            string permissionsPath = basePath + ".Permissions";
            string groupsPath = target is IPermissionGroup ? basePath + ".ParentGroups" : basePath + ".Groups";

            if (!config.ChildExists(permissionsPath))
            {
                config.CreateSection(permissionsPath, SectionType.Array);
                config[permissionsPath].Set(new string[0]);
            }

            if (!config.ChildExists(groupsPath))
            {
                config.CreateSection(groupsPath, SectionType.Array);
                config[groupsPath].Set(new string[0]);
            }

            return config[basePath];
        }

        private IConfigurationSection GetGroupsSection(IPermissible target)
        {
            return GetConfigSection(target)[target is IPermissionGroup ? "ParentGroups" : "Groups"];
        }


        private void GuardLoaded()
        {
            if (GroupsConfig == null || (GroupsConfig.Root != null && !GroupsConfig.Root.IsLoaded))
                throw new Exception("Groups config not loaded!");

            if (PlayersConfig == null || (PlayersConfig.Root != null && !PlayersConfig.Root.IsLoaded))
                throw new Exception("Players config has not been loaded");
        }

        private void GuardPermissible(IPermissible permissible)
        {
            if (!SupportsPermissible(permissible))
                throw new NotSupportedException(permissible.GetType().FullName + " is not supported!");
        }
    }
}