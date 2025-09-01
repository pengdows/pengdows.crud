#!/bin/bash

# Oracle Container Manager
# Usage: ./oracle-manager.sh [start|stop|status|logs|test]

CONTAINER_NAME="oracle-xe-testbed"
IMAGE_NAME="oracle/database:18.4.0-xe"
ORACLE_PASSWORD="mysecretpassword"
HOST_PORT="1521"
CONTAINER_PORT="1521"

case "$1" in
    start)
        echo "🔄 Starting Oracle container..."
        
        # Check if container already exists
        if docker ps -a --format 'table {{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
            echo "📦 Container ${CONTAINER_NAME} already exists"
            if docker ps --format 'table {{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
                echo "✅ Container is already running"
                exit 0
            else
                echo "🔄 Starting existing container..."
                docker start ${CONTAINER_NAME}
            fi
        else
            echo "🆕 Creating new Oracle container..."
            docker run -d \
                --name ${CONTAINER_NAME} \
                -p ${HOST_PORT}:${CONTAINER_PORT} \
                -e ORACLE_PWD=${ORACLE_PASSWORD} \
                -e ORACLE_CHARACTERSET=AL32UTF8 \
                ${IMAGE_NAME}
        fi
        
        echo "⏳ Waiting for Oracle to be ready (this takes 2-5 minutes)..."
        echo "💡 You can monitor progress with: ./oracle-manager.sh logs"
        echo "💡 Check status with: ./oracle-manager.sh status"
        
        # Wait for "DATABASE IS READY TO USE!" message
        timeout 600 bash -c 'until docker logs oracle-xe-testbed 2>&1 | grep -q "DATABASE IS READY TO USE!"; do sleep 5; echo -n "."; done'
        
        if [ $? -eq 0 ]; then
            echo ""
            echo "✅ Oracle is ready!"
            echo "🧪 You can now run tests with: INCLUDE_ORACLE=true dotnet run"
        else
            echo ""
            echo "⏰ Timeout waiting for Oracle (10 minutes). Check logs: ./oracle-manager.sh logs"
            exit 1
        fi
        ;;
        
    stop)
        echo "🛑 Stopping Oracle container..."
        docker stop ${CONTAINER_NAME}
        echo "🗑️  Removing Oracle container..."
        docker rm ${CONTAINER_NAME}
        echo "✅ Oracle container stopped and removed"
        ;;
        
    status)
        if docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | grep -q "^${CONTAINER_NAME}"; then
            echo "✅ Oracle container is running:"
            docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | grep "^${CONTAINER_NAME}"
            
            # Check if database is ready
            if docker logs ${CONTAINER_NAME} 2>&1 | grep -q "DATABASE IS READY TO USE!"; then
                echo "✅ Oracle database is ready for connections"
                echo "🧪 Run tests with: INCLUDE_ORACLE=true dotnet run"
            else
                echo "⏳ Oracle is still initializing..."
                echo "💡 Monitor with: ./oracle-manager.sh logs"
            fi
        elif docker ps -a --format 'table {{.Names}}\t{{.Status}}' | grep -q "^${CONTAINER_NAME}"; then
            echo "⏸️  Oracle container exists but is stopped:"
            docker ps -a --format 'table {{.Names}}\t{{.Status}}' | grep "^${CONTAINER_NAME}"
            echo "🔄 Start it with: ./oracle-manager.sh start"
        else
            echo "❌ Oracle container does not exist"
            echo "🆕 Create it with: ./oracle-manager.sh start"
        fi
        ;;
        
    logs)
        echo "📋 Oracle container logs (last 50 lines):"
        echo "==============================================="
        if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
            docker logs --tail 50 ${CONTAINER_NAME}
        else
            echo "❌ Container ${CONTAINER_NAME} does not exist"
        fi
        ;;
        
    test)
        echo "🧪 Testing Oracle connection..."
        
        # Check if container is running
        if ! docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
            echo "❌ Oracle container is not running. Start it with: ./oracle-manager.sh start"
            exit 1
        fi
        
        # Test connection using sqlplus in container
        echo "🔗 Testing connection to Oracle..."
        docker exec ${CONTAINER_NAME} bash -c "echo 'SELECT 1 FROM DUAL;' | sqlplus -s system/${ORACLE_PASSWORD}@XE" > /dev/null 2>&1
        
        if [ $? -eq 0 ]; then
            echo "✅ Oracle connection successful!"
            echo "🧪 Ready to run: INCLUDE_ORACLE=true dotnet run"
        else
            echo "❌ Oracle connection failed. Database may still be initializing."
            echo "💡 Check logs: ./oracle-manager.sh logs"
            exit 1
        fi
        ;;
        
    *)
        echo "Oracle Container Manager"
        echo ""
        echo "Usage: $0 {start|stop|status|logs|test}"
        echo ""
        echo "Commands:"
        echo "  start   - Start Oracle container (creates if needed, waits for ready)"
        echo "  stop    - Stop and remove Oracle container"  
        echo "  status  - Show container status and readiness"
        echo "  logs    - Show recent Oracle logs"
        echo "  test    - Test Oracle database connection"
        echo ""
        echo "Examples:"
        echo "  $0 start              # Start Oracle and wait for it to be ready"
        echo "  $0 status             # Check if Oracle is running and ready"
        echo "  INCLUDE_ORACLE=true dotnet run  # Run tests with Oracle"
        echo "  $0 stop               # Clean up Oracle container"
        exit 1
        ;;
esac