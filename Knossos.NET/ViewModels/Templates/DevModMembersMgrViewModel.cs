using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Knossos.NET.ViewModels
{
    public partial class DevModMembersMgrViewModel : ViewModelBase
    {
        /****/
        public partial class MemberItem : ObservableObject
        {
            [ObservableProperty]
            internal ModMember? modMember;
            private DevModMembersMgrViewModel? memberMgr;
            [ObservableProperty]
            internal ObservableCollection<ComboBoxItem> roleItems = new ObservableCollection<ComboBoxItem>();
            [ObservableProperty]
            internal int roleIndex = -1;
            [ObservableProperty]
            internal bool isEnabled = false;
            [ObservableProperty]
            internal bool isReadOnly = true;

            public MemberItem(DevModMembersMgrViewModel memberMgr)
            {
                IsReadOnly = false;
                IsEnabled = true;
                this.memberMgr = memberMgr;
                ModMember = new ModMember();
                ModMember.role = ModMemberRole.Tester;
                FillRoles();
            }

            public MemberItem(ModMember modMember, DevModMembersMgrViewModel memberMgr) 
            {
                this.modMember = modMember;
                this.memberMgr = memberMgr;
                IsReadOnly = true;
                if (modMember.user != Nebula.userName)
                {
                    isEnabled = true;
                }
                FillRoles();
            }

            private void FillRoles()
            {
                foreach (var role in Enum.GetValues(typeof(ModMemberRole)))
                {
                    var item = new ComboBoxItem();
                    item.Content = role.ToString();
                    item.DataContext = role;
                    RoleItems.Add(item);
                    if (ModMember != null && ModMember.role == (ModMemberRole)role)
                    {
                        RoleIndex = RoleItems.IndexOf(item);
                        item.IsSelected = true;
                    }
                }
            }

            internal ModMember? GetMember()
            {
                var newRole = RoleItems.FirstOrDefault(x=>x.IsSelected);
                if (ModMember != null && newRole != null && newRole.DataContext != null)
                    ModMember.role = (ModMemberRole)newRole.DataContext;
                return ModMember;
            }

            internal void Delete()
            {
                if(memberMgr != null)
                    memberMgr.DeleteMember(this);
            }
        }
        /****/
        
        private DevModEditorViewModel? editor;
        [ObservableProperty]
        internal ObservableCollection<MemberItem> memberItems = new ObservableCollection<MemberItem>();
        [ObservableProperty]
        internal bool buttonsEnabled = true;
        [ObservableProperty]
        internal bool loading = true;
        [ObservableProperty]
        internal bool showLoginError = false;

        public DevModMembersMgrViewModel() 
        { 
        }

        public DevModMembersMgrViewModel(DevModEditorViewModel editor)
        {
            this.editor = editor;
        }

        public async void UpdateUI()
        {
            if (editor != null && !MemberItems.Any())
            {
                if (Nebula.userIsLoggedIn) {
                    ButtonsEnabled = true;
                    ShowLoginError = false;
                    var members = await Nebula.GetTeamMembers(editor.ActiveVersion.id).ConfigureAwait(false);
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        if (members != null)
                        {
                            foreach (var member in members)
                            {
                                MemberItems.Add(new MemberItem(member, this));
                            }
                        }
                        else
                        {
                            _ = MessageBox.Show(MainWindow.instance!, "An error has ocurred while retrieving the mod member list. The log may provide more information.", "Error", MessageBox.MessageBoxButtons.OK);
                        }
                    });
                } else {
                    ButtonsEnabled = false;
                    ShowLoginError = true;
                }
            }
            Dispatcher.UIThread.Invoke(() =>
            {
                Loading = false;
            });
        }

        internal async void Save()
        {
            try
            {
                if (editor != null)
                {
                    var members = MemberItems.Select(x => x.GetMember()).ToList();
                    bool currentUserIsFound = false;
                    foreach (var member in members.ToList())
                    {
                        if (member!.user == Nebula.userName)
                        {
                            currentUserIsFound = true;
                        }
                        if (member.user.Replace(" ", "") == string.Empty)
                        {
                            var item = MemberItems.FirstOrDefault(x=>x.ModMember == member);
                            if(item != null)
                            {
                                MemberItems.Remove(item);
                            }
                            members.Remove(member);
                        }
                    }
                    if (!currentUserIsFound)
                    {
                        _ = MessageBox.Show(MainWindow.instance!, "For some reason your nebula user name: " + Nebula.userName + ". Was not found in this mod team member list. You cant save changes in this condition.", "Error", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    ButtonsEnabled = false;
                    var result = await Nebula.UpdateTeamMembers(editor.ActiveVersion.id, members.ToArray()!).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(async() => { 
                        if(result != null)
                        {
                            if(result == "ok")
                            {
                                await MessageBox.Show(MainWindow.instance!, "Mod members updated successfully!", "Save Changes", MessageBox.MessageBoxButtons.OK);
                            }
                            else
                            {
                                await MessageBox.Show(MainWindow.instance!, "An error has ocurred while updating members, no changes were saved. Reason: " + result, "Error", MessageBox.MessageBoxButtons.OK);
                            }
                        }
                        else
                        {
                            await MessageBox.Show(MainWindow.instance!, "An error has ocurred while updating members, no changes were saved. Reason: Unknown", "Error", MessageBox.MessageBoxButtons.OK);
                        }
                        ButtonsEnabled = true;
                    }).ConfigureAwait(false);
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModMembersMgrViewModel.Save", ex);
            }
        }

        internal void NewMember()
        {
            MemberItems.Add(new MemberItem(this));
        }

        internal void DeleteMember(MemberItem member)
        {
            MemberItems.Remove(member);
        }
    }
}
