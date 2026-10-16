/**
 * 控制台遥控主界面。
 */
package com.wechatlike.remote

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.app.Activity
import org.json.JSONObject

class ControlActivity : Activity() {

    private lateinit var ip: String
    private lateinit var port: String
    private lateinit var header: TextView
    private lateinit var taskContainer: LinearLayout
    private lateinit var logView: TextView
    private lateinit var taskSel: Button

    private val handler = Handler(Looper.getMainLooper())
    private val taskViews = LinkedHashMap<String, TaskView>()
    private var taskIds = listOf<String>()
    private var currentLogTask = ""
    private var alive = false

    private class TaskView(
        val root: LinearLayout,
        val dot: TextView,
        val name: TextView,
        val desc: TextView,
        val btn: Button
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ip = intent.getStringExtra("ip") ?: ""
        port = intent.getStringExtra("port") ?: "8765"
        alive = true

        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(36, 48, 36, 36)
        }

        header = TextView(this).apply {
            textSize = 15f
            setPadding(0, 0, 0, 20)
            layout.addView(this)
        }

        Button(this).apply {
            text = "断开连接"
            setOnClickListener {
                alive = false
                startActivity(Intent(this@ControlActivity, ConnectActivity::class.java))
                finish()
            }
            layout.addView(this)
        }

        taskContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, 24, 0, 0)
            layout.addView(this)
        }

        TextView(this).apply {
            text = "运行日志"
            textSize = 15f
            setPadding(0, 12, 0, 8)
            layout.addView(this)
        }

        taskSel = Button(this).apply {
            text = "选择任务"
            layout.addView(this)
        }

        val scroll = ScrollView(this).apply {
            layout.addView(this)
        }
        logView = TextView(this).apply {
            textSize = 11f
            setPadding(16, 16, 16, 16)
            setTextColor(0xFF1F2937.toInt())
            scroll.addView(this)
        }

        setContentView(layout)

        taskSel.setOnClickListener {
            if (taskIds.isEmpty()) return@setOnClickListener
            val items = taskIds.toTypedArray()
            val names = taskViews.values.map { it.name.text.toString() }.toTypedArray()
            android.app.AlertDialog.Builder(this)
                .setTitle("选择日志任务")
                .setItems(names) { _, which -> currentLogTask = taskIds[which] }
                .show()
        }

        handler.post(refreshRunnable)
    }

    private val refreshRunnable = object : Runnable {
        override fun run() {
            if (!alive) return
            Api.status(ip, port) { st ->
                if (st == null) {
                    runOnUiThread {
                        header.text = "● 已断开（电脑端服务无响应）\n$ip:$port"
                    }
                } else {
                    runOnUiThread { render(st) }
                }
            }
            if (currentLogTask.isNotEmpty()) {
                Api.logs(ip, port, currentLogTask, 40) { lines ->
                    runOnUiThread {
                        if (alive) logView.text = lines.joinToString("\n")
                    }
                }
            }
            handler.postDelayed(this, 2500)
        }
    }

    private fun render(st: JSONObject) {
        val remote = st.optJSONObject("remote")
        val tasks = st.optJSONArray("tasks")
        header.text = "● 已连接  $ip:$port\n运行中任务 " +
            (0 until (tasks?.length() ?: 0)).count { tasks!!.optJSONObject(it).optBoolean("running") } + " 个"

        if (tasks == null) return
        val ids = ArrayList<String>()
        for (i in 0 until tasks.length()) {
            val t = tasks.getJSONObject(i)
            val id = t.getString("id")
            ids.add(id)
            val name = t.getString("name")
            val desc = t.getString("desc")
            val running = t.getBoolean("running")
            var tv = taskViews[id]
            if (tv == null) {
                tv = buildTaskView(id, name, desc)
                taskViews[id] = tv
                taskContainer.addView(tv.root)
            }
            tv.name.text = name
            tv.desc.text = desc
            tv.dot.text = if (running) "●" else "○"
            tv.dot.setTextColor(if (running) 0xFF07C160.toInt() else 0xFF9CA3AF.toInt())
            tv.btn.text = if (running) "停止" else "开始"
            tv.btn.setOnClickListener {
                Api.task(ip, port, if (running) "stop" else "start", id) { _, _ -> }
            }
        }
        taskIds = ids
        if (currentLogTask.isEmpty() && ids.isNotEmpty()) currentLogTask = ids[0]
    }

    private fun buildTaskView(id: String, name: String, desc: String): TaskView {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(28, 20, 28, 20)
            background = android.graphics.drawable.GradientDrawable().apply {
                cornerRadius = 24f
                setColor(0xFFFFFFFF.toInt())
                setStroke(2, 0xFFE5E7EB.toInt())
            }
            val lp = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            )
            lp.setMargins(0, 0, 0, 24)
            layoutParams = lp
        }
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            card.addView(this)
        }
        val dot = TextView(this).apply {
            text = "○"
            textSize = 16f
            setPadding(0, 0, 12, 0)
            row.addView(this)
        }
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            row.addView(this)
        }
        val nameTv = TextView(this).apply {
            text = name
            textSize = 15f
            col.addView(this)
        }
        val descTv = TextView(this).apply {
            text = desc
            textSize = 12f
            setTextColor(0xFF6B7280.toInt())
            col.addView(this)
        }
        val btn = Button(this).apply {
            text = "开始"
            row.addView(this)
        }
        return TaskView(card, dot, nameTv, descTv, btn)
    }

    override fun onDestroy() {
        alive = false
        super.onDestroy()
    }
}
