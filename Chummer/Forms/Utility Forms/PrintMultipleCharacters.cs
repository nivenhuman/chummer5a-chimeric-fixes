/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chummer
{
    public partial class PrintMultipleCharacters : Form
    {
        private CancellationTokenSource _objPrinterCancellationTokenSource;
        private Task _tskPrinter;
        private Character[] _aobjCharacters;
        private CharacterSheetViewer _frmPrintView;

        #region Control Events

        public PrintMultipleCharacters()
        {
            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
        }

        private async void PrintMultipleCharacters_Load(object sender, EventArgs e)
        {
            dlgOpenFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Chum5") + '|' +
                                 await LanguageManager.GetStringAsync("DialogFilter_All");
        }

        private async void PrintMultipleCharacters_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_objPrinterCancellationTokenSource?.IsCancellationRequested == false)
            {
                _objPrinterCancellationTokenSource.Cancel(false);
                _objPrinterCancellationTokenSource.Dispose();
                _objPrinterCancellationTokenSource = null;
            }
            await CleanUpOldCharacters();
        }

        private async void cmdSelectCharacter_Click(object sender, EventArgs e)
        {
            // Add the selected Files to the list of characters to print.
            if (dlgOpenFile.ShowDialog(this) == DialogResult.OK)
            {
                await CancelPrint();
                foreach (string strFileName in dlgOpenFile.FileNames)
                {
                    TreeNode objNode = new TreeNode
                    {
                        Text = Path.GetFileName(strFileName) ?? await LanguageManager.GetStringAsync("String_Unknown"),
                        Tag = strFileName
                    };
                    await treCharacters.DoThreadSafeAsync(x => x.Nodes.Add(objNode));
                }

                if (_frmPrintView != null)
                    await StartPrint();
            }
        }

        private async void cmdDelete_Click(object sender, EventArgs e)
        {
            if (await treCharacters.DoThreadSafeFuncAsync(x => x.SelectedNode) != null)
            {
                await CancelPrint();
                await treCharacters.DoThreadSafeAsync(x => x.SelectedNode.Remove());
                if (_frmPrintView != null)
                    await StartPrint();
            }
        }

        private async void cmdPrint_Click(object sender, EventArgs e)
        {
            await StartPrint();
        }

        private async ValueTask CancelPrint(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_objPrinterCancellationTokenSource?.IsCancellationRequested == false)
            {
                _objPrinterCancellationTokenSource.Cancel(false);
                _objPrinterCancellationTokenSource.Dispose();
                _objPrinterCancellationTokenSource = null;
            }
            token.ThrowIfCancellationRequested();
            try
            {
                if (_tskPrinter?.IsCompleted == false)
                    await Task.WhenAll(_tskPrinter, cmdPrint.DoThreadSafeAsync(x => x.Enabled = true, token),
                                       prgProgress.DoThreadSafeAsync(x => x.Value = 0, token));
                else
                    await Task.WhenAll(cmdPrint.DoThreadSafeAsync(x => x.Enabled = true, token),
                                       prgProgress.DoThreadSafeAsync(x => x.Value = 0, token));
            }
            catch (OperationCanceledException)
            {
                // Swallow this
            }
        }

        private async ValueTask StartPrint(CancellationToken token = default)
        {
            await CancelPrint(token);
            token.ThrowIfCancellationRequested();
            _objPrinterCancellationTokenSource = new CancellationTokenSource();
            CancellationToken objToken = _objPrinterCancellationTokenSource.Token;
            _tskPrinter = Task.Run(() => DoPrint(objToken), objToken);
        }

        private async Task DoPrint(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using (await CursorWait.NewAsync(this, true, token))
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    int intNodesCount = await treCharacters.DoThreadSafeFuncAsync(x => x.Nodes.Count, token);
                    await Task.WhenAll(cmdPrint.DoThreadSafeAsync(x => x.Enabled = false, token),
                                       prgProgress.DoThreadSafeAsync(objBar =>
                                       {
                                           objBar.Value = 0;
                                           objBar.Maximum = intNodesCount;
                                       }, token));
                    token.ThrowIfCancellationRequested();
                    // Parallelized load because this is one major bottleneck.
                    Character[] lstCharacters = new Character[intNodesCount];
                    Task<Character>[] tskLoadingTasks = new Task<Character>[intNodesCount];
                    for (int i = 0; i < tskLoadingTasks.Length; ++i)
                    {
                        string strLoopFile = await treCharacters.DoThreadSafeFuncAsync(x => x.Nodes[i].Tag.ToString(), token);
                        tskLoadingTasks[i]
                            = Task.Run(() => InnerLoad(strLoopFile, token), token);
                    }

                    async Task<Character> InnerLoad(string strLoopFile, CancellationToken innerToken = default)
                    {
                        innerToken.ThrowIfCancellationRequested();

                        Character objReturn;
                        using (LoadingBar frmLoadingBar = await Program.CreateAndShowProgressBarAsync(strLoopFile, Character.NumLoadingSections))
                            objReturn = await Program.LoadCharacterAsync(strLoopFile, string.Empty, false, false, frmLoadingBar);
                        bool blnLoadSuccessful = objReturn != null;
                        innerToken.ThrowIfCancellationRequested();

                        if (blnLoadSuccessful)
                            await prgProgress.DoThreadSafeAsync(() => ++prgProgress.Value, innerToken);
                        return objReturn;
                    }

                    await Task.WhenAll(tskLoadingTasks);
                    token.ThrowIfCancellationRequested();
                    for (int i = 0; i < lstCharacters.Length; ++i)
                        lstCharacters[i] = await tskLoadingTasks[i];
                    token.ThrowIfCancellationRequested();
                    await CleanUpOldCharacters(token);
                    token.ThrowIfCancellationRequested();
                    _aobjCharacters = lstCharacters;

                    if (_frmPrintView == null)
                    {
                        _frmPrintView = await this.DoThreadSafeFuncAsync(() => new CharacterSheetViewer(), token);
                        await _frmPrintView.SetSelectedSheet("Game Master Summary", token);
                        await _frmPrintView.SetCharacters(token, _aobjCharacters);
                        await _frmPrintView.DoThreadSafeAsync(x => x.Show(), token);
                    }
                    else
                    {
                        await _frmPrintView.SetCharacters(token, _aobjCharacters);
                        await _frmPrintView.DoThreadSafeAsync(x => x.Activate(), token);
                    }
                }
                finally
                {
                    await Task.WhenAll(cmdPrint.DoThreadSafeAsync(x => x.Enabled = true, token),
                                       prgProgress.DoThreadSafeAsync(x => x.Value = 0, token));
                }
            }
        }

        private async ValueTask CleanUpOldCharacters(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(_aobjCharacters?.Length > 0))
                return;
            // Dispose of any characters who were previous loaded but are no longer needed and don't have any linked characters
            bool blnAnyChanges = true;
            while (blnAnyChanges)
            {
                token.ThrowIfCancellationRequested();
                blnAnyChanges = false;
                foreach (Character objCharacter in _aobjCharacters)
                {
                    if (!await Program.OpenCharacters.ContainsAsync(objCharacter) ||
                        await Program.MainForm.OpenCharacterForms.AnyAsync(x => x.CharacterObject == objCharacter, token) ||
                        await Program.OpenCharacters.AnyAsync(x => x.LinkedCharacters.Contains(objCharacter), token))
                        continue;
                    blnAnyChanges = true;
                    await Program.OpenCharacters.RemoveAsync(objCharacter);
                    await objCharacter.DisposeAsync();
                    token.ThrowIfCancellationRequested();
                }
            }
        }

        #endregion Control Events
    }
}
