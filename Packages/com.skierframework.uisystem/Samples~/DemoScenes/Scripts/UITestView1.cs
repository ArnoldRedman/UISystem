using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkierFramework
{
    public class UITestView1 : UIView
    {
        #region 控件绑定变量声明，自动生成请勿手改
#pragma warning disable 0649
        [ControlBinding]
        private Button ButtonClose;
        [ControlBinding]
        private Button ButtonJump;
        [ControlBinding]
        private TextMeshProUGUI TextState;

#pragma warning restore 0649
        #endregion


        public override void OnInit(UIControlData uIControlData, UIViewController controller)
        {
            base.OnInit(uIControlData, controller);

            ButtonClose.AddClick(() =>
            {
                UIManager.Instance.Close(Controller.uiType);
            });
            ButtonJump.AddClick(() =>
            {
                UIManager.Instance.JumpUI(UIType.UITestView1, userData, UIType.UITestView2, null);
            });
        }

        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            Invoke("TestText", 0.1f);
        }

        public void TestText()
        {
            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < UIManager.Instance.UIJumpDatas.Count; i++)
            {
                if (i == 0)
                    stringBuilder.Append($"{UIManager.Instance.UIJumpDatas[i].curUIType} -> {UIManager.Instance.UIJumpDatas[i].nextUIType}");
                else
                    stringBuilder.Append($" -> {UIManager.Instance.UIJumpDatas[i].nextUIType}");
            }
            TextState.text = stringBuilder.ToString();
        }

        public override void OnAddListener()
        {
            base.OnAddListener();
        }

        public override void OnRemoveListener()
        {
            base.OnRemoveListener();
        }

        public override void OnClose()
        {
            base.OnClose();
        }
    }
}
